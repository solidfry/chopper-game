"""
Chassis specs: the numbers that shape each family. "body": utility (default), attack (chunky tandem-cockpit hull; "snout": sensor or grille, "taper" < 1 narrows the
tops), assault (blocky tapered blocks, grille snout) or heavy
(heavy lifter; "rotor_layout": single, tandem, coaxial or quad). Change a value or the seed and run the generator again.
Slot lists match the game's ChassisDefinition slot order (Editor/Setup/LoadoutAssets.cs), so sockets get the same
types and indices.
"""

CHASSIS = [
    dict(name="Scout", seed=11, width=1.35, height=1.45, front=3.0, boom=3.6, wings=False, engines=1,
         sensor=True, armour=0.2, skids=True,
         slots=["Nose", "Pylon", "Utility", "Rotor", "Engine", "Armour"]),
    dict(name="Gunship", seed=21, body="attack", width=1.8, height=1.75, front=3.6, boom=4.4, wings=True, wing_span=1.4, engines=2,
         sensor=False, armour=0.5, skids=True,
         slots=["Nose", "Pylon", "Pylon", "Utility", "Rotor", "Engine", "Armour"]),
    dict(name="Assault", seed=31, body="attack", snout="grille", taper=0.85, width=2.3, height=2.05, front=3.9, boom=4.6, wings=True, wing_span=1.6, engines=2,
         sensor=True, armour=1.0, skids=True,
         slots=["Nose", "Pylon", "Pylon", "Turret", "Utility", "Rotor", "Engine", "Armour"]),
    dict(name="Lifter", seed=41, body="heavy", rotor_layout="tandem", width=3.0, height=3.1, front=4.6, boom=4.4,
         wings=False, engines=2,
         sensor=False, armour=0.4, skids=True,
         slots=["Nose", "Pylon", "Utility", "Utility", "Winch", "Rotor", "Engine", "Armour"]),
    # Arcus Aerodyne line (sleek.py): one more chassis per class, same slots as the class above.
    dict(name="Wisp", maker="arcus", body="wisp", seed=111, width=1.3, height=1.4, front=3.1, boom=3.8, engines=1,
         sensor=True, armour=0.2, skids=True,
         slots=["Nose", "Pylon", "Utility", "Rotor", "Engine", "Armour"]),
    dict(name="Lance", maker="arcus", body="lance", seed=121, width=1.6, height=1.55, front=4.2, boom=4.8, wings=True,
         wing_span=1.25, engines=2, sensor=False, armour=0.5, skids=False,
         slots=["Nose", "Pylon", "Pylon", "Utility", "Rotor", "Engine", "Armour"]),
    dict(name="Aegis", maker="arcus", body="aegis", seed=131, width=2.3, height=1.9, front=4.0, boom=4.2, wings=True,
         wing_span=1.6, engines=2, sensor=True, armour=1.0, skids=False,
         slots=["Nose", "Pylon", "Pylon", "Turret", "Utility", "Rotor", "Engine", "Armour"]),
    dict(name="Halo", maker="arcus", body="halo", rotor_layout="coaxial", seed=141, width=2.6, height=2.5, front=4.6,
         boom=4.0, wings=False, engines=2, sensor=False, armour=0.4, skids=False,
         slots=["Nose", "Pylon", "Utility", "Utility", "Winch", "Rotor", "Engine", "Armour"]),
]

# Modules shown on each chassis in the preview render (by slot, in socket order). Armour has no model (stats only).
PREVIEW_LOADOUTS = {
    "Scout": {"Nose": ["Gun_Minigun"], "Pylon": ["Rack_HomingMissiles"], "Utility": ["Utility_Flares"],
              "Rotor": ["Rotor_Agile"], "Engine": ["Engine_Turbine"]},
    "Gunship": {"Nose": ["Gun_Minigun"], "Pylon": ["Pod_Rockets", "Rack_HomingMissiles"], "Utility": ["Utility_Flares"],
                "Rotor": ["Rotor_Standard"], "Engine": ["Engine_Standard"]},
    "Assault": {"Nose": ["Gun_Autocannon"], "Pylon": ["Pod_Swarm", "Rack_ClusterMissile"], "Turret": ["Turret_Flak"],
                "Utility": ["Utility_Shield"], "Rotor": ["Rotor_HeavyLift"], "Engine": ["Engine_Boost"]},
    "Lifter": {"Nose": ["Gun_PulseLaser"], "Pylon": ["Rack_GroundSeeker"], "Utility": ["Utility_RepairDrone", "Utility_EMP"],
               "Winch": ["Winch_Heavy"], "Rotor": ["Rotor_HeavyLift"], "Engine": ["Engine_Standard"]},
    "Wisp": {"Nose": ["Gun_Minigun"], "Pylon": ["Rack_HomingMissiles"], "Utility": ["Utility_Flares"],
             "Rotor": ["Rotor_Agile"], "Engine": ["Engine_Turbine"]},
    "Lance": {"Nose": ["Gun_PulseLaser"], "Pylon": ["Rack_HomingMissiles", "Rack_HomingMissiles"],
              "Utility": ["Utility_Flares"], "Rotor": ["Rotor_Agile"], "Engine": ["Engine_Boost"]},
    "Aegis": {"Nose": ["Gun_Autocannon"], "Pylon": ["Pod_Swarm", "Pod_Swarm"], "Turret": ["Turret_Flak"],
              "Utility": ["Utility_Shield"], "Rotor": ["Rotor_Standard"], "Engine": ["Engine_Standard"]},
    "Halo": {"Nose": ["Gun_PulseLaser"], "Pylon": ["Rack_GroundSeeker"], "Utility": ["Utility_RepairDrone", "Utility_EMP"],
             "Winch": ["Winch_Heavy"], "Rotor": ["Rotor_HeavyLift"], "Engine": ["Engine_Turbine"]},
}
