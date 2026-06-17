"""NormalBake reference oracle — headless Blender tangent-space normal bake.

Blender is the primary oracle because it is free, scriptable, deterministic, and uses the canonical
**MikkTSpace** tangent basis (the same basis NormalBake targets). This script drives a fully
programmatic "Selected to Active" normal bake and writes a 16-bit PNG golden.

Usage (Blender 3.6+ / 4.x):

    blender --background --python bake_normal.py -- \
        --high high.obj --low low.obj --out golden.png \
        --res 1024 --samples 1 --cage-extrusion 0.05 --max-ray-distance 0.0 \
        --margin 16 --convention opengl

Determinism: CPU device, a fixed Cycles sample count, and the **Extend** bake margin (the only correct
margin for tangent-space normals — see ../../Agents/Documents/05-uvs-and-dilation.md §4.2). The bake
image is tagged Non-Color (linear) and saved as 16-bit PNG to match NormalBake's default output.
"""

import argparse
import sys

import bpy


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    p = argparse.ArgumentParser(description="Blender headless normal-map bake.")
    p.add_argument("--high", required=True, help="high-poly OBJ")
    p.add_argument("--low", required=True, help="UV-unwrapped low-poly OBJ")
    p.add_argument("--out", required=True, help="output golden PNG")
    p.add_argument("--res", type=int, default=1024)
    p.add_argument("--samples", type=int, default=1)
    p.add_argument("--cage-extrusion", type=float, default=0.05)
    p.add_argument("--max-ray-distance", type=float, default=0.0)
    p.add_argument("--margin", type=int, default=16)
    p.add_argument("--convention", choices=["opengl", "directx"], default="opengl")
    return p.parse_args(argv)


def import_obj(path):
    """Imports an OBJ and returns the new mesh objects."""
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=path)
    return [o for o in bpy.data.objects if o not in before and o.type == "MESH"]


def main():
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)

    high_objs = import_obj(args.high)
    low_objs = import_obj(args.low)
    if not low_objs:
        raise RuntimeError("low-poly import produced no mesh object")
    low = low_objs[0]

    # A material whose active image node receives the bake.
    image = bpy.data.images.new("nb_bake", width=args.res, height=args.res, alpha=False, float_buffer=True)
    image.colorspace_settings.name = "Non-Color"  # linear / vector data
    mat = bpy.data.materials.new("nb_bake_mat")
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = image
    mat.node_tree.nodes.active = tex
    low.data.materials.clear()
    low.data.materials.append(mat)

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = args.samples

    bake = scene.render.bake
    bake.use_selected_to_active = True
    bake.cage_extrusion = args.cage_extrusion
    if args.max_ray_distance > 0.0:
        bake.max_ray_distance = args.max_ray_distance
    bake.margin = args.margin
    bake.margin_type = "EXTEND"  # required for tangent-space normals
    bake.normal_g = "NEG_Y" if args.convention == "directx" else "POS_Y"

    bpy.ops.object.select_all(action="DESELECT")
    for o in high_objs:
        o.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low

    bpy.ops.object.bake(
        type="NORMAL",
        normal_space="TANGENT",
        margin=args.margin,
        margin_type="EXTEND",
        use_selected_to_active=True,
        cage_extrusion=args.cage_extrusion,
    )

    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.color_depth = "16"
    image.save_render(filepath=args.out, scene=scene)
    print(f"NormalBake oracle: wrote {args.out}")


if __name__ == "__main__":
    main()
