"""
Scale reference for the previews: the game's own choppers (PartForge kit FBX files and textures from
Assets/Chopper/Art/Kits/<kit>), with a rotor on every rotor socket and the nose gun. Preview only; nothing is exported.
"""
import json
import math
import os

import bpy

from .pf import geo

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
KIT = "Kit02"
COLLECTION = "CityForge: choppers"
LOADOUTS = {"Gunship": ("Rotor_Standard", "Gun_Minigun"), "Scout": ("Rotor_Agile", "Gun_Minigun"),
            "Lifter": ("Rotor_HeavyLift", "Gun_PulseLaser"), "Assault": ("Rotor_HeavyLift", "Gun_Autocannon")}


def available(kit=KIT):
    return os.path.exists(os.path.join(REPO, "Assets", "Chopper", "Art", "Kits", kit, f"{kit}.manifest.json"))


class Library:
    def __init__(self, kit=KIT):
        self.kit = kit
        self.folder = os.path.join(REPO, "Assets", "Chopper", "Art", "Kits", kit)
        with open(os.path.join(self.folder, f"{kit}.manifest.json")) as handle:
            self.manifest = json.load(handle)
        self.parts = {part["name"]: part for part in self.manifest["parts"]}
        self.materials = {}
        self.meshes = {}
        collection = bpy.data.collections.get(COLLECTION) or bpy.data.collections.new(COLLECTION)
        if collection.name not in bpy.context.scene.collection.children:
            bpy.context.scene.collection.children.link(collection)
        self.collection = collection

    def ground_offset(self, chassis):
        """Height of the chassis origin above the ground when it stands on its gear."""
        name = f"Chassis_{chassis}"
        for texture_set in self.manifest["textureSets"]:
            if texture_set["name"] == chassis:
                return -texture_set["heightRange"][0]
        return 1.5 if name else 0.0

    def _material(self, set_name):
        if set_name in self.materials:
            return self.materials[set_name]
        material = bpy.data.materials.new(f"CF {self.kit}_{set_name}")
        material.use_nodes = True
        nodes, links = material.node_tree.nodes, material.node_tree.links
        bsdf = nodes["Principled BSDF"]
        bsdf.inputs["Roughness"].default_value = 0.6
        colour_file = os.path.join(self.folder, "Textures", f"{self.kit}_{set_name}_factory_Colour.png")
        glow_file = os.path.join(self.folder, "Textures", f"{self.kit}_{set_name}_Emission.png")
        if os.path.exists(colour_file):
            node = nodes.new("ShaderNodeTexImage")
            node.image = bpy.data.images.load(colour_file, check_existing=True)
            links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        else:
            bsdf.inputs["Base Color"].default_value = (0.1, 0.11, 0.1, 1.0)
        if os.path.exists(glow_file):
            node = nodes.new("ShaderNodeTexImage")
            node.image = bpy.data.images.load(glow_file, check_existing=True)
            links.new(node.outputs["Color"], bsdf.inputs["Emission Color"])
            bsdf.inputs["Emission Strength"].default_value = 4.0
        self.materials[set_name] = material
        return material

    def _mesh(self, part_name):
        """The part's mesh (imported once from its FBX), with the kit texture material."""
        if part_name in self.meshes:
            return self.meshes[part_name]
        part = self.parts[part_name]
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(self.folder, part["file"]))
        new = [obj for obj in bpy.data.objects if obj not in before]
        source = next(obj for obj in new if obj.type == "MESH")
        bpy.context.view_layer.update()
        mesh = source.data.copy()
        mesh.transform(source.matrix_world)  # bake any import axis turn into the mesh
        material = self._material(part.get("textureSet", "Modules"))
        mesh.materials.clear()
        mesh.materials.append(material)
        for obj in new:
            bpy.data.objects.remove(obj, do_unlink=True)
        self.meshes[part_name] = mesh
        return mesh

    def chopper(self, chassis, position, yaw=0.0, name=None):
        """A chassis with its rotor(s) and nose gun at a Unity position; yaw in degrees (0 = nose to +Z)."""
        rotor, gun = LOADOUTS.get(chassis, LOADOUTS["Gunship"])
        holder = bpy.data.objects.new(name or f"Chopper_{chassis}", None)
        self.collection.objects.link(holder)
        holder.location = geo.to_blender(position)
        holder.rotation_euler = (0.0, 0.0, math.radians(-yaw))
        body = bpy.data.objects.new(f"{holder.name}_Body", self._mesh(f"Chassis_{chassis}"))
        self.collection.objects.link(body)
        body.parent = holder
        for socket in self.parts[f"Chassis_{chassis}"]["sockets"]:
            module = {"Rotor": rotor, "Nose": gun}.get(socket["slot"])
            if module is None or module not in self.parts:
                continue
            obj = bpy.data.objects.new(f"{holder.name}_{socket['name']}", self._mesh(module))
            self.collection.objects.link(obj)
            obj.parent = holder
            obj.location = geo.to_blender(socket["position"])
            if socket["slot"] == "Rotor":
                obj.rotation_euler = (0.0, 0.0, math.radians(17.0 + 40.0 * socket["index"]))
        return holder

    def objects(self):
        return list(self.collection.objects)
