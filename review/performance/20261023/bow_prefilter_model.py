#!/usr/bin/env python3
"""Standalone model of the Bows35 FindTarget conditional reorder. Not Valheim runtime."""
import math
import random

def select(candidates, new):
    best_angle, best_depth, chosen = 12.0, float("inf"), None
    enemy_calls = linecasts = 0
    for i, c in enumerate(candidates):
        if not c["valid"]:
            continue
        if not new:
            enemy_calls += 1
            if not c["enemy"]:
                continue
        x, y, z = c["pos"]
        if z <= 0 or z > 80:
            continue
        angle = math.degrees(math.atan2(math.hypot(x, y), z))
        if angle > best_angle + 0.01 or (abs(angle - best_angle) < 0.01 and z >= best_depth):
            continue
        if new:
            enemy_calls += 1
            if not c["enemy"]:
                continue
        linecasts += 1
        if c["blocked"]:
            continue
        chosen, best_angle, best_depth = i, angle, z
    return chosen, enemy_calls, linecasts

rng = random.Random(20261023)
old_calls = new_calls = old_los = new_los = 0
for case in range(1000):
    candidates = [
        {
            "valid": rng.random() < .89,
            "enemy": rng.random() < .55,
            "pos": (rng.uniform(-100, 100), rng.uniform(-10, 10), rng.uniform(-90, 110)),
            "blocked": rng.random() < .3,
        }
        for _ in range(rng.randint(0, 320))
    ]
    old, new = select(candidates, False), select(candidates, True)
    assert old[0] == new[0], (case, old, new)
    assert old[2] == new[2], (case, old, new)
    old_calls += old[1]
    new_calls += new[1]
    old_los += old[2]
    new_los += new[2]
print(f"PASS 1000 cases, enemy calls {old_calls}->{new_calls}, LOS calls {old_los}->{new_los}")
