"""Print Blender object hierarchy, transforms, and world-space bounds as JSON."""

import json

import bpy
from mathutils import Vector


rows = []
for obj in bpy.context.scene.objects:
    row = {
        "name": obj.name,
        "type": obj.type,
        "parent": obj.parent.name if obj.parent else None,
        "location": list(obj.location),
        "rotationEuler": list(obj.rotation_euler),
        "scale": list(obj.scale),
    }
    if obj.type == "MESH":
        corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
        row["worldBoundsMin"] = [
            min(corner[axis] for corner in corners) for axis in range(3)
        ]
        row["worldBoundsMax"] = [
            max(corner[axis] for corner in corners) for axis in range(3)
        ]
        row["vertices"] = len(obj.data.vertices)
        row["polygons"] = len(obj.data.polygons)
    rows.append(row)

print("OBJECT_AUDIT_JSON=" + json.dumps(rows))
