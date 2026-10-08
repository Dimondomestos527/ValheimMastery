import copy
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import mastery_update as u

def package(files, variant='Client'):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w') as z:
        for name, data in files.items():
            z.writestr(name, data)
    manifest = {'schema': 1, 'product': 'ValheimMastery', 'variant': variant, 'version': 'test', 'files': [{'path': name, 'size': len(data), 'sha256': hashlib.sha256(data).hexdigest()} for name, data in files.items()]}
    return manifest, stream.getvalue()

class UpdaterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.root.joinpath('other-mod.dll').write_bytes(b'keep')
        self.root.joinpath('player.cfg').write_bytes(b'config')
        self.running = patch.object(u, 'running', return_value=False)
        self.running.start()
    def tearDown(self):
        self.running.stop()
        self.temp.cleanup()
    def update(self, files):
        u.install(self.root, *package(files))
    def test_install_and_owned_asset_removal(self):
        self.update({'ValheimMastery.dll': b'old', 'assets/old.png': b'oldart'})
        self.root.joinpath('assets/unmanaged.png').write_bytes(b'keep')
        self.update({'ValheimMastery.dll': b'new', 'assets/new.png': b'newart'})
        self.assertFalse(self.root.joinpath('assets/old.png').exists())
        self.assertEqual(self.root.joinpath('assets/unmanaged.png').read_bytes(), b'keep')
        self.assertEqual(self.root.joinpath('ValheimMastery.dll').read_bytes(), b'new')
        self.assertEqual(self.root.joinpath('other-mod.dll').read_bytes(), b'keep')
        self.assertEqual(self.root.joinpath('player.cfg').read_bytes(), b'config')
    def test_corrupt_hash_changes_nothing(self):
        self.root.joinpath('ValheimMastery.dll').write_bytes(b'old')
        m, z = package({'ValheimMastery.dll': b'new'})
        m['files'][0]['sha256'] = '0' * 64
        with self.assertRaises(ValueError): u.install(self.root, m, z)
        self.assertEqual(self.root.joinpath('ValheimMastery.dll').read_bytes(), b'old')
    def test_zip_traversal_extra_rejected(self):
        m, z = package({'ValheimMastery.dll': b'new', '../escape.png': b'bad'})
        with self.assertRaises(ValueError): u.install(self.root, m, z)
    def test_config_path_rejected(self):
        with self.assertRaises(ValueError): self.update({'ValheimMastery.dll': b'new', 'player.cfg': b'bad'})
    def test_variant_mismatch(self):
        self.update({'ValheimMastery.dll': b'client'})
        with self.assertRaises(ValueError): u.install(self.root, *package({'ValheimMastery.dll': b'server'}, 'Server'))
        self.assertEqual(self.root.joinpath('ValheimMastery.dll').read_bytes(), b'client')
    def test_rollback_after_first_replacement(self):
        self.update({'ValheimMastery.dll': b'old', 'assets/old.png': b'art'})
        previous = self.root.joinpath('.mastery-updates/installed.json').read_bytes()
        real = u.os.replace
        calls = [0]
        def fail_second(a, b):
            calls[0] += 1
            if calls[0] == 2: raise OSError('simulated failure')
            return real(a, b)
        with patch.object(u.os, 'replace', side_effect=fail_second):
            with self.assertRaises(OSError): self.update({'ValheimMastery.dll': b'new', 'assets/new.png': b'new'})
        self.assertEqual(self.root.joinpath('ValheimMastery.dll').read_bytes(), b'old')
        self.assertFalse(self.root.joinpath('assets/new.png').exists())
        self.assertEqual(self.root.joinpath('.mastery-updates/installed.json').read_bytes(), previous)
    def test_running_game(self):
        with patch.object(u, 'running', return_value=True):
            with self.assertRaises(RuntimeError): self.update({'ValheimMastery.dll': b'new'})
        self.assertFalse(self.root.joinpath('ValheimMastery.dll').exists())
    def test_lock_blocks_concurrent_run(self):
        self.root.joinpath('.mastery-updates/lock').mkdir(parents=True)
        with self.assertRaises(FileExistsError): self.update({'ValheimMastery.dll': b'new'})
    def test_actual_distribution_matches_installed(self):
        folder = Path(__file__).resolve().parent.parent / 'distribution'
        for variant in ['client', 'server']:
            m = json.loads(folder.joinpath(variant + '.json').read_text())
            data = folder.joinpath(m['archive']).read_bytes()
            self.assertEqual(u.digest(data), m['archive_sha256'])
            with tempfile.TemporaryDirectory() as d:
                u.install(Path(d), m, data)
                self.assertEqual(u.digest(Path(d).joinpath('ValheimMastery.dll').read_bytes()), m['files'][0]['sha256'])
    def test_full_download_flow_pins_one_commit(self):
        manifest, archive = package({'ValheimMastery.dll': b'new'})
        manifest['archive'] = 'client-test.zip'
        manifest['archive_sha256'] = u.digest(archive)
        sha = 'a' * 40
        responses = [json.dumps({'sha': sha}).encode(), json.dumps(manifest).encode(), archive]
        with patch.object(u, 'download', side_effect=responses) as fetch, patch.object(u.sys, 'argv', ['updater', '--target', str(self.root)]):
            u.main()
        self.assertEqual(fetch.call_count, 3)
        self.assertIn('/' + sha + '/distribution/', fetch.call_args_list[1].args[0])
        self.assertIn('/' + sha + '/distribution/', fetch.call_args_list[2].args[0])
        self.assertEqual(self.root.joinpath('ValheimMastery.dll').read_bytes(), b'new')
    def test_case_only_rename_rejected(self):
        self.update({'ValheimMastery.dll': b'old', 'assets/Old.png': b'art'})
        with self.assertRaises(ValueError): self.update({'ValheimMastery.dll': b'new', 'assets/old.png': b'new'})
        self.assertEqual(self.root.joinpath('ValheimMastery.dll').read_bytes(), b'old')
    def test_steam_root_resolves_plugin_directory(self):
        self.root.joinpath('valheim.exe').write_bytes(b'marker')
        self.root.joinpath('BepInEx').mkdir()
        target = u.installation_directory(self.root)
        self.assertEqual(target, self.root / 'BepInEx/plugins/ValheimMastery')
        u.install(target, *package({'ValheimMastery.dll': b'new'}))
        self.assertFalse(self.root.joinpath('ValheimMastery.dll').exists())
        self.assertEqual(target.joinpath('ValheimMastery.dll').read_bytes(), b'new')
    def test_steam_root_without_bepinex_refused(self):
        self.root.joinpath('valheim.exe').write_bytes(b'marker')
        with self.assertRaises(RuntimeError): u.installation_directory(self.root)
        self.assertFalse(self.root.joinpath('BepInEx').exists())
    def test_server_root_and_plugin_folder(self):
        self.assertEqual(u.installation_directory(self.root), self.root)
        self.root.joinpath('valheim_server.exe').write_bytes(b'marker')
        self.root.joinpath('BepInEx').mkdir()
        self.assertEqual(u.installation_directory(self.root), self.root / 'BepInEx/plugins/ValheimMastery')

if __name__ == '__main__': unittest.main()
