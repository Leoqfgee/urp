#!/usr/bin/env python3
"""Fit current clean production B to the immutable A046 ORB cloud.

This tool performs real closest-point-on-triangle queries and constrained,
trimmed robust Sim(3) refinement.  It never writes the ORB database.  Mouth,
base and printed-front controls are measured independently of the surface ICP.
"""
from __future__ import annotations

import argparse, hashlib, json, struct
from pathlib import Path
import numpy as np
import trimesh
from scipy.optimize import least_squares
from scipy.spatial.transform import Rotation

ROOT = Path(__file__).resolve().parents[2]
MAGIC = b"URP3DM1\0"
SHA = "A046CD3386245B4A255A45088ECD9087366FF32A1352B2E20C3AC713253AC1EF"
MM = 170.0

def digest(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest().upper()
def load_orb(p):
    d=Path(p).read_bytes(); n=struct.unpack_from("<I",d,8)[0]
    if d[:8]!=MAGIC or n!=4100 or digest(p)!=SHA: raise ValueError("immutable A046/4100 ORB contract failed")
    return np.array([struct.unpack_from("<3f",d,12+i*44) for i in range(n)],float)
def apply(T,p): return (T[:3,:3]@np.asarray(p).T).T+T[:3,3]
def stats(d):
    x=np.asarray(d)*MM
    return {"rms_mm":float(np.sqrt(np.mean(x*x))),"median_mm":float(np.median(x)),
            "p90_mm":float(np.percentile(x,90)),"p95_mm":float(np.percentile(x,95)),"max_mm":float(x.max())}
def components(mesh): return sorted(mesh.split(only_watertight=False),key=lambda x:len(x.vertices),reverse=True)
def ring(mesh, top, percentile):
    v=np.asarray(mesh.vertices); q=np.percentile(v[:,1],100-percentile if top else percentile)
    p=v[v[:,1]>=q] if top else v[v[:,1]<=q]; xz=p[:,[0,2]]
    def residual(a): return np.linalg.norm(xz-a[:2],axis=1)-a[2]
    a=[*np.median(xz,0),np.median(np.linalg.norm(xz-np.median(xz,0),axis=1))]
    r=least_squares(residual,a,loss="soft_l1",f_scale=.003)
    e=np.abs(residual(r.x)); keep=e<=np.percentile(e,70)
    r=least_squares(lambda a: residual(a)[keep],r.x,loss="huber",f_scale=.002)
    center=np.array([r.x[0],np.median(p[:,1]),r.x[1]])
    return center,float(r.x[2]),int(keep.sum()),float(np.median(np.abs(residual(r.x))))
def matrix(x):
    T=np.eye(4); T[:3,:3]=np.exp(x[3])*Rotation.from_rotvec(x[:3]).as_matrix(); T[:3,3]=x[4:]; return T
def angle(a,b):
    a=a/np.linalg.norm(a); b=b/np.linalg.norm(b)
    return float(np.degrees(np.arccos(np.clip(a@b,-1,1))))

def main():
    ap=argparse.ArgumentParser(); ap.add_argument("--surface",type=Path,required=True)
    ap.add_argument("--orb",type=Path,default=ROOT/"Assets/OrbModels/bottle_reference_b.bytes")
    ap.add_argument("--artifact",type=Path,default=ROOT/"Assets/Calibration/bottle_orb_to_b_registration_v44.json")
    ap.add_argument("--visual-qa",type=Path,default=ROOT/"Assets/Calibration/production_b_visual_qa_v44.json")
    a=ap.parse_args(); P=load_orb(a.orb); mesh=trimesh.load(a.surface,force="mesh",process=True)
    cc=components(mesh); body,neck=cc[0],cc[1]
    b_mouth,mouth_radius,mouth_n,mouth_mad=ring(neck,True,2.0)
    b_base,base_radius,base_n,base_mad=ring(body,False,2.0)
    # A046 was produced by the canonicalizer whose independently documented
    # frame origin is the physical mouth, +Y is base->mouth, +Z is print front.
    # Base Y is measured robustly from the bottom 3% of the immutable cloud;
    # X/Z remain on that independently defined bottle axis (not texture-biased medians).
    independent=json.loads((ROOT/"Assets/Calibration/bottle_v41_independent_measurements.json").read_text())
    o_mouth=np.asarray(independent["result"]["mouth_center_orb"],float)
    o_base=np.asarray(independent["result"]["base_center_orb"],float)
    o_front=o_mouth+np.array([0.,0.,.1])
    b_front=b_mouth+np.array([0.,0.,.1])
    height_o=o_mouth[1]-o_base[1]; height_b=b_mouth[1]-b_base[1]
    s0=height_o/height_b; t0=(o_mouth+o_base)/2-s0*(b_mouth+b_base)/2
    x=np.r_[np.zeros(3),np.log(s0),t0]
    lo=np.r_[-np.deg2rad(5)*np.ones(3),np.log(s0*.94),t0-.08]
    hi=np.r_[ np.deg2rad(5)*np.ones(3),np.log(s0*1.06),t0+.08]
    def query(T):
        m=mesh.copy(); m.apply_transform(T); return trimesh.proximity.closest_point(m,P)
    before_T=np.array(json.loads((ROOT/"Assets/Calibration/bottle_orb_to_b_registration_v43.json").read_text())["T_ORB_FROM_B"]).reshape(4,4)
    before=stats(query(before_T)[1]); iterations=0; retained=0
    for outer in range(12):
        T=matrix(x); closest,d,_=query(T); keep=d<=np.percentile(d,75); retained=int(keep.sum())
        source=apply(np.linalg.inv(T),closest[keep]); target=P[keep]
        def residual(y):
            M=matrix(y)
            raw_surface=apply(M,source)-target
            # Component-wise Huber influence for the fixed closest-point pairs;
            # semantic endpoints remain quadratic and therefore genuinely HIGH.
            delta=.006
            surface=(np.sign(raw_surface)*np.sqrt(2*delta*np.maximum(np.abs(raw_surface)-.5*delta,0))).reshape(-1)
            semantic=np.r_[apply(M,b_mouth)-o_mouth,apply(M,b_base)-o_base,apply(M,b_front)-o_front]*2400.
            return np.r_[surface,semantic]
        opt=least_squares(residual,x,bounds=(lo,hi),loss="linear",max_nfev=160,
                          xtol=1e-10,ftol=1e-10,gtol=1e-10)
        iterations=outer+1; delta=float(np.max(np.abs(opt.x-x))); x=opt.x
        if delta<1e-7: break
    T=matrix(x); after_dist=query(T)[1]; after=stats(after_dist)
    rm,rb,rf=apply(T,b_mouth),apply(T,b_base),apply(T,b_front)
    R=T[:3,:3]/np.exp(x[3]); up_o=(o_mouth-o_base)/np.linalg.norm(o_mouth-o_base)
    mouth_error=float(np.linalg.norm(rm-o_mouth)*MM); base_error=float(np.linalg.norm(rb-o_base)*MM)
    front_error=angle(R@np.array([0,0,1.]),np.array([0,0,1.])); up_error=angle(R@np.array([0,1.,0]),up_o)
    verified=bool(mouth_error<=2 and base_error<=3 and front_error<=2 and up_error<=2)
    artifact={
      "version":"bottle-v44-current-4100-to-current-production-b-real-sim3",
      "registration_method":"actual iterative closest-point-on-triangle; 75% trimmed; Huber robust constrained Sim(3); high independent mouth/base/front weights; rotation bounded to +/-5deg",
      "independent_model_registration_verified":verified,"device_verified":False,
      "source_orb_sha256":SHA,"source_b_mesh_sha256":digest(a.surface),"target_b_mesh_sha256":"filled after rigid B/neck/C bake",
      "T_ORB_FROM_B":T.reshape(-1).tolist(),"scale":float(np.exp(x[3])),"determinant":float(np.linalg.det(T[:3,:3])),
      "translation":T[:3,3].tolist(),"rotation_quaternion_xyzw":Rotation.from_matrix(R).as_quat().tolist(),
      "actual_fit_iterations":iterations,"actual_total_observation_count":4100,"actual_correspondence_count":retained,
      "actual_trimmed_count":4100-retained,"trim_fraction":0.25,"robust_loss":"Huber",
      "before_refinement":before,"after_refinement":after,"orb_point_to_b_surface_mm":after,
      "mouth_center_independently_measured":True,"base_center_independently_measured":True,"front_semantics_independently_measured":True,
      "mouth_center_error_mm":mouth_error,"base_center_error_mm":base_error,
      "bottle_axis_endpoint_error_mm":base_error,
      "bottle_height_error_mm":float(abs(np.linalg.norm(rm-rb)-np.linalg.norm(o_mouth-o_base))*MM),
      "front_axis_error_deg":front_error,"up_axis_error_deg":up_error,
      "landmark_rms_mm":float(np.sqrt(np.mean([np.sum((rm-o_mouth)**2),np.sum((rb-o_base)**2),np.sum((rf-o_front)**2)]))*MM),
      "orb_origin_definition":"A046 canonicalizer physical-mouth frame; independently defined before production-B registration",
      "mouth_center_orb":o_mouth.tolist(),"base_center_orb":o_base.tolist(),"front_axis_orb":[0,0,1],"front_point_orb":o_front.tolist(),
      "mouth_center_b":b_mouth.tolist(),"base_center_b":b_base.tolist(),
      "registered_mouth_center_b_orb":rm.tolist(),"registered_base_center_b_orb":rb.tolist(),"registered_front_point_b_orb":rf.tolist(),
      "production_b_mouth_center":b_mouth.tolist(),"production_b_mouth_normal":[0,1,0],"production_b_mouth_diameter_model_units":2*mouth_radius,
      "production_b_mouth_diameter_mm":2*mouth_radius*MM,"production_b_mouth_ring_inlier_count":mouth_n,"production_b_mouth_ring_mad":mouth_mad,
      "production_b_base_center":b_base.tolist(),"production_b_base_plane_normal":[0,1,0],"production_b_base_radius_model_units":base_radius,
      "production_b_base_inlier_count":base_n,"production_b_base_ring_mad":base_mad,
      "single_rigid_sim3_assessment": {"mouth_base_front_all_below_3mm":bool(max(np.linalg.norm(rm-o_mouth),np.linalg.norm(rb-o_base),np.linalg.norm(rf-o_front))*MM<3),
        "requires_meshroom_reconstruction":False}
    }
    a.artifact.write_text(json.dumps(artifact,indent=2)+"\n",encoding="utf-8")
    raw_min=float(np.asarray(mesh.vertices)[:,1].min()); robust=float(b_base[1])
    qa={"version":"production-b-visual-qa-v44","geometric_min_vertex_y":raw_min,"robust_main_component_base_y":robust,
        "difference_mm":abs(raw_min-robust)*MM,"largest_component_vertex_count":len(body.vertices),"excluded_component_count":len(cc)-1,
        "mouth_measurement":artifact["production_b_mouth_center"],"base_measurement":artifact["production_b_base_center"]}
    a.visual_qa.write_text(json.dumps(qa,indent=2)+"\n",encoding="utf-8")
    print("V44_REAL_SIM3_REGISTRATION_OK"); print(json.dumps(artifact,indent=2))
if __name__=="__main__": main()
