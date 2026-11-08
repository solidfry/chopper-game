"""Summarise match logs (JSON lines from MatchLogSystem; see Assets/Chopper/Simulation/Session/MatchLogSystem.cs).

    python Tools/MatchLogs/summarize.py Logs/Matches/*.jsonl
    python Tools/MatchLogs/summarize.py some_server.log      # a raw server log: lines with "[Event] " are used

Prints per file: session, players and loadouts, weapons (shots, hits, accuracy, damage, kills, mean range),
kills by kind, missiles (end and decoys), flares, server load. Standard library only.
"""
import collections
import json
import statistics
import sys

PREFIX = "[Event] "


def events(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if PREFIX in line:
                line = line[line.index(PREFIX) + len(PREFIX):]
            if not line.startswith("{"):
                continue
            try:
                yield json.loads(line)
            except json.JSONDecodeError:
                pass


def summarize(path):
    session, maps, matches, loads = None, [], [], []
    players = {}
    shots = collections.Counter()
    hits = collections.defaultdict(lambda: {"n": 0, "dmg": 0.0, "dist": []})
    kills_by_weapon = collections.Counter()
    kills_by_kind = collections.Counter()
    kd = collections.defaultdict(lambda: [0, 0])
    lives = []
    missiles = collections.Counter()
    decoys = 0
    flares = collections.Counter()
    last_t = 0.0

    for e in events(path):
        last_t = max(last_t, e.get("t", 0))
        kind = e.get("e")
        if kind == "session":
            session = e
        elif kind == "map":
            maps.append(e.get("scene"))
        elif kind == "match":
            matches.append(e)
        elif kind == "join":
            players[e["id"]] = f'{e.get("chassis")} / {e.get("pilot")} / {", ".join(e.get("modules", []))}'
        elif kind == "shots":
            shots[e["weapon"]] += e["n"]
        elif kind == "hits":
            if e.get("id", 0) == 0 or e["id"] == e.get("target"):
                key = "(environment) " + e["kind"]
            elif not e["weapon"]:
                key = "(chopper) " + e["kind"]  # rams, damage over time
            else:
                key = e["weapon"]
            h = hits[key]
            h["n"] += e["n"]
            h["dmg"] += e["dmg"]
            if e.get("dist", -1) >= 0:
                h["dist"].append(e["dist"])
        elif kind == "kill":
            weapon = e.get("weapon") or e.get("kind")
            kills_by_weapon[weapon if e.get("killer") else "(no killer) " + e.get("kind", "")] += 1
            kills_by_kind[e.get("kind")] += 1
            if e.get("killer"):
                kd[e["killer"]][0] += 1
            kd[e["victim"]][1] += 1
            if "life" in e:
                lives.append(e["life"])
        elif kind == "missile":
            missiles[(e.get("weapon"), e.get("end"))] += 1
        elif kind == "decoy":
            decoys += 1
        elif kind == "flare":
            flares[e["id"]] += 1
        elif kind == "load":
            loads.append(e)

    print(f"=== {path}")
    if session:
        print(f'  build {session.get("version")}  request {session.get("request") or "local"}  '
              f'{session.get("city", "")} {session.get("country", "")}  tick {session.get("tickRate")}  cpus {session.get("cpus")}')
    print(f"  maps {maps}  length {last_t / 60:.1f} min  matches {[m.get('phase') for m in matches]}")
    print("  players:")
    for pid, loadout in sorted(players.items()):
        k, d = kd.get(pid, [0, 0])
        print(f"    {pid}: {k} kills / {d} deaths, flares {flares.get(pid, 0)}  [{loadout}]")
    print("  weapons (hits on choppers and targets; accuracy = hits / shots, splash can pass 100%):")
    for weapon in sorted(set(shots) | {k for k in hits if not k.startswith("(")}):
        h = hits.get(weapon, {"n": 0, "dmg": 0.0, "dist": []})
        n = shots.get(weapon, 0)
        acc = f"{100 * h['n'] / n:.0f}%" if n else "-"
        rng = f"{statistics.mean(h['dist']):.0f} m" if h["dist"] else "-"
        print(f"    {weapon:28} shots {n:6}  hits {h['n']:6}  acc {acc:>5}  dmg {h['dmg']:8.0f}  "
              f"kills {kills_by_weapon.get(weapon, 0):3}  range {rng}")
    for key in sorted(k for k in hits if k.startswith("(")):
        print(f"    {key:28} hits {hits[key]['n']:6}  dmg {hits[key]['dmg']:8.0f}")
    print(f"  kills by kind {dict(kills_by_kind)}; no-killer deaths {sum(v for k, v in kills_by_weapon.items() if k.startswith('(no'))}")
    if lives:
        print(f"  life length: median {statistics.median(lives):.0f} s, mean {statistics.mean(lives):.0f} s")
    if missiles or decoys:
        print(f"  missiles {dict(missiles)}; decoyed by flares {decoys}")
    if loads:
        busy = [l for l in loads if l.get("players", 0) > 0] or loads
        print(f"  server load ({len(busy)} samples with players): CPU mean {statistics.mean(l['cpu'] for l in busy):.0f}% "
              f"max {max(l['cpu'] for l in busy):.0f}% of a core; ticks/s min {min(l['ticks'] for l in busy):.1f}; "
              f"worst frame {max(l['worstMs'] for l in busy):.0f} ms; hitches {sum(l['hitches'] for l in busy)}; "
              f"memory max {max(l['memMB'] for l in busy)} MB; players max {max(l['players'] for l in busy)}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    for p in sys.argv[1:]:
        summarize(p)
