#!/usr/bin/env python3
"""Add non-emissive backing and an invisible registration proxy in v44 frame."""
from __future__ import annotations
import argparse,json,math,sys
from pathlib import Path
import bpy
from mathutils import Matrix,Vector
def args():
    av=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    p=argparse.ArgumentParser();p.add_argument('--fbx-output',type=Path,required=True);p.add_argument('--blend-output',type=Path,required=True)
    p.add_argument('--registration-proxy',type=Path,required=True);p.add_argument('--artifact',type=Path,required=True);return p.parse_args(av)
def mat(name,color):
    m=bpy.data.materials.get(name) or bpy.data.materials.new(name);m.use_nodes=True;m.diffuse_color=color
    n=next(x for x in m.node_tree.nodes if x.type=='BSDF_PRINCIPLED');n.inputs['Base Color'].default_value=color;n.inputs['Metallic'].default_value=0;n.inputs['Roughness'].default_value=.42
    e=n.inputs.get('Emission Color') or n.inputs.get('Emission');
    if e:e.default_value=(0,0,0,1)
    s=n.inputs.get('Emission Strength');
    if s:s.default_value=0
    return m
def main():
    a=args(); j=json.loads(a.artifact.read_text());root=bpy.data.objects['BottleRepairRoot'];body=bpy.data.objects['DamagedBottleB'];neck=bpy.data.objects['ReferenceNeckProxyB'];cap=bpy.data.objects['BottleCapC']
    bpy.ops.wm.ply_import(filepath=str(a.registration_proxy));proxy=bpy.context.active_object;proxy.name='BottleTrackingRegistrationProxy';proxy.parent=root;proxy.matrix_parent_inverse=Matrix.Identity(4);proxy.hide_render=True;proxy.hide_viewport=True
    mouth=Vector(j['registered_mouth_center_b_orb']);base=Vector(j['registered_base_center_b_orb']);h=mouth.y-base.y
    rows=[0,.06,.18,.36,.74,.88,.94,.98,1.0]; radii=[(.15,.14),(.19,.17),(.20,.18),(.19,.17),(.18,.16),(.16,.14),(.13,.115),(.10,.09),(.085,.08)]
    seg=128;verts=[];faces=[]
    for f,(rx,rz) in zip(rows,radii):
      y=base.y+h*f;cx=base.x+(mouth.x-base.x)*f;cz=base.z+(mouth.z-base.z)*f
      for i in range(seg):
       q=2*math.pi*i/seg;verts.append((cx+rx*math.cos(q),y,cz+rz*math.sin(q)))
    for row in range(len(rows)-1):
      for i in range(seg):
       n=(i+1)%seg;a0=row*seg+i;b0=row*seg+n;c=(row+1)*seg+n;d=(row+1)*seg+i;faces.append((a0,b0,c,d))
    faces+=[tuple(reversed(range(seg))),tuple((len(rows)-1)*seg+i for i in range(seg))]
    me=bpy.data.meshes.new('ProductionBCleanBackingShellMesh');me.from_pydata(verts,[],faces);me.materials.append(mat('ProductionBackingWhite',(.78,.79,.74,1)));me.materials.append(mat('ProductionBackingGreen',(.18,.46,.08,1)))
    shell=bpy.data.objects.new('ProductionBCleanBackingShell',me);bpy.context.collection.objects.link(shell);shell.parent=body;shell.matrix_parent_inverse=Matrix.Identity(4)
    for p in me.polygons:p.material_index=1 if sum(me.vertices[i].co.y for i in p.vertices)/len(p.vertices)<base.y+.25*h else 0;p.use_smooth=True
    a.blend_output.parent.mkdir(parents=True,exist_ok=True);bpy.ops.wm.save_as_mainfile(filepath=str(a.blend_output));bpy.ops.object.select_all(action='DESELECT')
    for o in (root,body,neck,shell,proxy,cap):o.select_set(True)
    bpy.context.view_layer.objects.active=root;bpy.ops.export_scene.fbx(filepath=str(a.fbx_output),use_selection=True,object_types={'EMPTY','MESH'},apply_unit_scale=True,bake_space_transform=False,axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,path_mode='COPY',embed_textures=True)
    print('PRODUCTION_B_BACKING_SHELL_V44_OK')
if __name__=='__main__':main()
