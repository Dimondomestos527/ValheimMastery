"""Read-only UnityFS/serialized type-tree probe; no Unity or game is executed."""
import bisect
import collections
import json
import struct
import sys


def lz4(data, expected):
    result = bytearray()
    cursor = 0
    while cursor < len(data):
        token = data[cursor]
        cursor += 1
        length = token >> 4
        if length == 15:
            while True:
                extra = data[cursor]
                cursor += 1
                length += extra
                if extra != 255:
                    break
        result.extend(data[cursor:cursor + length])
        cursor += length
        if cursor == len(data):
            break
        distance = data[cursor] | data[cursor + 1] << 8
        cursor += 2
        length = (token & 15) + 4
        if (token & 15) == 15:
            while True:
                extra = data[cursor]
                cursor += 1
                length += extra
                if extra != 255:
                    break
        for _ in range(length):
            result.append(result[-distance])
    if len(result) != expected:
        raise ValueError((len(result), expected))
    return bytes(result)


class Reader:
    def __init__(self, data, endian="<"):
        self.data, self.pos, self.endian = data, 0, endian

    def read(self, n):
        value = self.data[self.pos:self.pos + n]
        self.pos += n
        return value

    def number(self, kind):
        return struct.unpack(self.endian + kind, self.read(struct.calcsize(kind)))[0]

    def text(self):
        end = self.data.index(0, self.pos)
        value = self.data[self.pos:end].decode("utf-8", errors="replace")
        self.pos = end + 1
        return value

    def align(self):
        self.pos = (self.pos + 3) & ~3


class Bundle:
    def __init__(self, path):
        self.file = open(path, "rb")
        r = Reader(self.file.read(512), ">")
        assert r.text() == "UnityFS"
        version = r.number("I")
        r.text()
        r.text()
        size, packed, unpacked, flags = r.number("Q"), r.number("I"), r.number("I"), r.number("I")
        start = (r.pos + 15) & ~15 if version >= 7 else r.pos
        self.file.seek(size - packed if flags & 128 else start)
        info = self.file.read(packed)
        info = lz4(info, unpacked) if flags & 63 in (2, 3) else info
        meta = Reader(info, ">")
        meta.read(16)
        count = meta.number("I")
        self.blocks, self.starts = [], []
        physical, logical = start + (0 if flags & 128 else packed), 0
        if flags & 512:
            physical = (physical + 15) & ~15
        for _ in range(count):
            uncompressed, compressed, mode = meta.number("I"), meta.number("I"), meta.number("H")
            self.starts.append(logical)
            self.blocks.append((physical, logical, uncompressed, compressed, mode))
            physical += compressed
            logical += uncompressed
        self.entries = []
        for _ in range(meta.number("I")):
            offset, length, entry_flags, name = meta.number("Q"), meta.number("Q"), meta.number("I"), meta.text()
            self.entries.append((offset, length, entry_flags, name))
        self.cache = collections.OrderedDict()

    def read(self, start, length):
        result = bytearray()
        while length > 0:
            index = bisect.bisect_right(self.starts, start) - 1
            physical, logical, size, packed, flags = self.blocks[index]
            if index not in self.cache:
                self.file.seek(physical)
                data = self.file.read(packed)
                self.cache[index] = lz4(data, size) if flags & 63 in (2, 3) else data
                if len(self.cache) > 24:
                    self.cache.popitem(last=False)
            data = self.cache[index]
            amount = min(length, size - (start - logical))
            result.extend(data[start - logical:start - logical + amount])
            start += amount
            length -= amount
        return bytes(result)


COMMON = {0: "AABB", 49: "Array", 55: "Base", 76: "bool", 81: "char", 86: "ColorRGBA", 96: "Component", 106: "data", 117: "double", 155: "first", 161: "float", 172: "GameObject", 222: "int", 231: "long long", 240: "map", 263: "MonoBehaviour", 277: "MonoScript", 288: "m_ByteSize", 299: "m_Curve", 307: "m_EditorClassIdentifier", 331: "m_EditorHideFlags", 349: "m_Enabled", 374: "m_GameObject", 395: "m_IsArray", 427: "m_Name", 434: "m_ObjectHideFlags", 490: "m_Script", 536: "Object", 543: "pair", 548: "PPtr<Component>", 564: "PPtr<GameObject>", 581: "PPtr<Material>", 596: "PPtr<MonoBehaviour>", 616: "PPtr<MonoScript>", 633: "PPtr<Object>", 718: "PPtr<Transform>", 741: "Quaternionf", 778: "second", 789: "short", 795: "size", 800: "SInt16", 807: "SInt32", 814: "SInt64", 821: "SInt8", 840: "string", 884: "Transform", 894: "TypelessData", 907: "UInt16", 914: "UInt32", 921: "UInt64", 928: "UInt8", 934: "unsigned int", 947: "unsigned long long", 966: "unsigned short", 981: "vector", 988: "Vector2f", 997: "Vector3f", 1006: "Vector4f", 1093: "m_CorrespondingSourceObject", 1121: "m_PrefabInstance", 1138: "m_PrefabAsset"}


class Assets:
    def __init__(self, bundle, entry):
        self.bundle, self.offset = bundle, entry[0]
        h = Reader(bundle.read(self.offset, 64), ">")
        metadata, size, self.version, data = [h.number("I") for _ in range(4)]
        endian = h.number("B")
        h.read(3)
        if self.version >= 22:
            metadata, size, data = h.number("I"), h.number("Q"), h.number("Q")
            h.read(8)
        self.data_offset, self.endian = self.offset + data, ">" if endian else "<"
        r = Reader(bundle.read(self.offset + h.pos, metadata), self.endian)
        self.unity, platform, has_tree = r.text(), r.number("i"), r.number("B")
        self.types = []
        for _ in range(r.number("i")):
            class_id, stripped, script_index = r.number("i"), r.number("B"), r.number("h")
            if class_id == 114:
                r.read(16)
            r.read(16)
            nodes = []
            if has_tree:
                count, strings_size = r.number("i"), r.number("i")
                for _ in range(count):
                    values = [r.number(k) for k in ("H", "B", "B", "I", "I", "i", "i", "i")]
                    if self.version >= 19:
                        r.read(8)
                    nodes.append(values)
                strings = r.read(strings_size)
                def resolve(n):
                    return COMMON.get(n & 0x7fffffff, "common_" + str(n & 0x7fffffff)) if n & 0x80000000 else strings[n:strings.index(0, n)].decode("utf-8", errors="replace")
                for node in nodes:
                    node[3], node[4] = resolve(node[3]), resolve(node[4])
            if self.version >= 21:
                r.read(4 * r.number("i"))
            self.types.append((class_id, nodes))
        self.objects = {}
        for _ in range(r.number("i")):
            r.align()
            path, position, length, type_id = r.number("q"), r.number("q" if self.version >= 22 else "I"), r.number("I"), r.number("i")
            self.objects[path] = (self.data_offset + position, length, type_id)

    def value(self, path):
        position, length, type_id = self.objects[path]
        class_id, flat = self.types[type_id]
        r = Reader(self.bundle.read(position, length), self.endian)
        def parse(index):
            version, level, type_flag, type_name, name, size, number, flags = flat[index]
            end = index + 1
            while end < len(flat) and flat[end][1] > level:
                end += 1
            primitives = {"float": "f", "double": "d", "int": "i", "SInt32": "i", "UInt32": "I", "unsigned int": "I", "UInt8": "B", "char": "B", "bool": "B", "SInt8": "b", "SInt16": "h", "short": "h", "UInt16": "H", "unsigned short": "H", "SInt64": "q", "long long": "q", "UInt64": "Q", "unsigned long long": "Q"}
            if type_name == "string":
                value = r.read(r.number("i")).decode("utf-8", errors="replace")
                r.align()
            elif type_name in primitives:
                value = r.number(primitives[type_name])
            elif type_name == "TypelessData":
                value = r.read(r.number("i")).hex()
            elif type_name == "Array":
                count = r.number("i")
                value = [parse(index + 2)[0] for _ in range(count)]
            elif end == index + 1:
                if size < 0:
                    raise ValueError((type_name, name, size))
                value = r.read(size).hex()
            else:
                value, child = {}, index + 1
                while child < end:
                    child_name = flat[child][4]
                    child_value, child = parse(child)
                    value[child_name] = child_value
                if list(value) == ["Array"]:
                    value = value["Array"]
            if flags & 0x4000:
                r.align()
            return value, end
        value, _ = parse(0)
        return class_id, value


if __name__ == "__main__":
    bundle = Bundle(sys.argv[1])
    print("entries", json.dumps(bundle.entries))
    for entry in bundle.entries:
        if entry[2] & 4:
            assets = Assets(bundle, entry)
            print("serialized", assets.unity, assets.version, len(assets.objects), len(assets.types))
            found = []
            if len(sys.argv) > 3:
                found = [int(v) for v in sys.argv[3].split(",")]
            else:
                for path, (_, _, type_id) in assets.objects.items():
                    if assets.types[type_id][0] != 1:
                        continue
                    _, value = assets.value(path)
                    if value.get("m_Name") in sys.argv[2].split(","):
                        print("GAMEOBJECT", path, json.dumps(value))
                        found.append(path)
            for path in found:
                todo, seen = [path], set()
                while todo:
                    item = todo.pop()
                    if item in seen or item not in assets.objects:
                        continue
                    seen.add(item)
                    class_id, value = assets.value(item)
                    summary = value
                    if class_id == 198:
                        summary = {k: value.get(k) for k in ("m_GameObject", "lengthInSec", "looping", "playOnAwake", "moveWithTransform", "scalingMode", "cullingMode")}
                        summary["InitialModule"] = {k: value.get("InitialModule", {}).get(k) for k in ("startLifetime", "startSpeed", "startSize", "startColor", "maxNumParticles")}
                        summary["EmissionModule"] = value.get("EmissionModule")
                        summary["ShapeModule"] = {k: value.get("ShapeModule", {}).get(k) for k in ("enabled", "type", "radius", "m_Scale")}
                    print("OBJECT", item, class_id, json.dumps(summary))
                    if class_id == 1:
                        todo.extend(v.get("component", v).get("m_PathID", 0) for v in value.get("m_Component", []))
                    elif class_id == 4:
                        todo.extend(v.get("m_PathID", 0) for v in value.get("m_Children", []))
                        todo.append(value.get("m_GameObject", {}).get("m_PathID", 0))
                    elif class_id == 114:
                        todo.append(value.get("m_Script", {}).get("m_PathID", 0))
