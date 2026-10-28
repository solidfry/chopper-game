"""
Chassis specs: the numbers that shape each family. Change a value or the seed and run the generator again.
Slot lists match the game's ChassisDefinition slot order (Editor/Setup/LoadoutAssets.cs), so sockets get the same
types and indices.
"""

CHASSIS = [
    dict(name="Scout", seed=11, width=1.35, height=1.45, front=3.0, boom=3.6, wings=False, engines=1,
         sensor=True, armour=0.2, skids=True,
         slots=["Nose", "Pylon", "Utility", "Rotor", "Engine", "Armour"]),
    dict(name="Gunship", seed=21, width=1.8, height=1.75, front=3.6, boom=4.4, wings=True, wing_span=1.4, engines=2,
         sensor=False, armour=0.5, skids=True,
         slots=["Nose", "Pylon", "Pylon", "Utility", "Rotor", "Engine", "Armour"]),
    dict(name="Assault", seed=31, width=2.3, height=2.05, front=3.9, boom=4.6, wings=True, wing_span=1.6, engines=2,
         sensor=True, armour=1.0, skids=True,
         slots=["Nose", "Pylon", "Pylon", "Turret", "Utility", "Rotor", "Engine", "Armour"]),
    dict(name="Lifter", seed=41, width=2.1, height=2.3, front=4.2, boom=5.2, wings=False, engines=2,
         sensor=False, armour=0.4, skids=True,
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
}
