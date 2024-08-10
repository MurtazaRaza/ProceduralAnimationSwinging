using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Wolfire
{
    public partial class GibbonControl : MonoBehaviour
    {
        // Simple rig point ids
        // I just remembered the int values and used them directly, but this is good for reference
        private const int p_shoulder_r = 0;
        private const int p_hand_r = 1;
        private const int p_shoulder_l = 2;
        private const int p_hand_l = 3;
        private const int p_base = 4;
        private const int p_head = 5;
        private const int p_neck = 6;
        private const int p_stomach = 7;
        private const int p_hip = 8;
        private const int p_groin = 9;
        private const int p_hip_r = 10;
        private const int p_foot_r = 11;
        private const int p_hip_l = 12;
        private const int p_foot_l = 13;

        private float bone_flex_value = 0.4f;
        
        
        public GameObject display_gibbon; // Character mesh and bone transforms

        // Initial point positions for IK
        private readonly float3[] arm_ik = new float3[3];

        // Various parameters that were being used to tune an animation
        private readonly float base_walk_height = 0.7f;

        private readonly Verlet.System branches = new();

        // Particle simulation systems
        private readonly Verlet.System completeVerletSimulationSystem = new();

        private readonly DebugInfo debug_info = new();
        private readonly MovementSystem displayMovementSystem = new();

        private readonly DisplayBody display_body = new();
        private readonly float gallop_arm_stride = 0.4f;
        private readonly float gallop_arm_stride_height = 0.4f;
        private readonly float gallop_height = 0.012f;
        private readonly float gallop_height_base = 0.8f;
        private readonly float gallop_height_offset = 0.6f;
        private readonly float gallop_hip_rotate = -1.3f;
        private readonly float gallop_offset = 0.55f; // For biped gallop
        private readonly float gallop_stride = 1.0f;
        private readonly float gallop_stride_height = 0.2f;

        private readonly MovementSystem jumpMovementSystem = new();
        private readonly float3[] leg_ik = new float3[3];
        private readonly float quad_gallop_body_compress_amount = 0.15f;
        private readonly float quad_gallop_body_compress_offset = 0.4f;
        private readonly float quad_gallop_offset = 0.25f; // For quadruped gallop
        private readonly MovementSystem swingMovementSystem = new();
        private readonly float tilt_offset = 0.81f;

        private readonly MovementSystem walkMovementSystem = new();

        private float
            body_compress_amount; // Used to shorten the distance between neck and hips if necessary, e.g. during quadruped gallop

        private float gallop_amount;
        private float gallop_lean = 1.5f;

        private bool b_in_air; // Are we currently jumping?

        private float
            in_air_amount; // Could be used to transition into and out of jumping, even though we just use 0 or 1 right now

        private float3
            jump_com_offset; // Use to preserve COM continuity when starting jumps, so it doesn't just warp to the simple_pos position

        private float3 jump_point; // Where jump started (at feet)
        private float jump_time; // At what Time.time did jump start?
        private float3 look_target; // For head look IK

        private float on_branch = 1f; // Is 1 if running on branch, 0 if swinging underneath it
        private float3 predicted_land_point; // Where will we land?
        private float predicted_land_time; // At what Time.time do we expect to land?
        private float quad_amount;

        // Simple character particle information
        private float3 simple_pos;
        private float3 simple_vel = float3.zero;
        private float skate_amount; // Used to interpolate into and out of sliding state

        // Time marker in animation sequences (so cycles can speed up and slow down without losing continuity)
        private float swing_time;
        private float walk_time;
        private bool b_wants_to_swing; // Whether we are transitioning to or from swinging

        private void Start()
        {
            // Starting point
            simple_pos = display_gibbon.transform.position;
            simple_pos[1] = 0f;
            simple_pos[2] = 0f;

            // Init hand positions
            for (var i = 0; i < 4; ++i)
            {
                displayMovementSystem.limb_targets[i] = simple_pos;
                walkMovementSystem.limb_targets[i] = simple_pos;
                swingMovementSystem.limb_targets[i] = simple_pos;
            }

            // Get transforms of each skeleton point
            Transform rootTransform = GameObject.Find("points").transform;
            Transform neckTransform = rootTransform.Find("neck");
            Transform stomachTransform = rootTransform.Find("stomach");
            Transform pelvisTransform = rootTransform.Find("pelvis");
            Transform groinTransform = rootTransform.Find("groin");
            Transform headTransform = rootTransform.Find("head");
            Transform shoulderTransform = rootTransform.Find("shoulder");
            Transform elbowTransform = rootTransform.Find("elbow");
            Transform gripTransform = rootTransform.Find("grip");
            Transform hipTransform = rootTransform.Find("hip");
            Transform kneeTransform = rootTransform.Find("knee");
            Transform footTransform = rootTransform.Find("foot");

            // Set up bind poses for each bone
            display_body.head.Bind(display_gibbon.transform.Find("DEF-head"));
            display_body.chest.Bind(display_gibbon.transform.Find("DEF-chest"));
            display_body.belly.Bind(display_gibbon.transform.Find("DEF-belly"));
            display_body.pelvis.Bind(display_gibbon.transform.Find("DEF-pelvis"));
            display_body.arm_top_l.Bind(display_gibbon.transform.Find("DEF-upper_arm_L"));
            display_body.arm_bottom_l.Bind(display_gibbon.transform.Find("DEF-forearm_L"));
            display_body.arm_top_r.Bind(display_gibbon.transform.Find("DEF-upper_arm_R"));
            display_body.arm_bottom_r.Bind(display_gibbon.transform.Find("DEF-forearm_R"));
            display_body.leg_top_l.Bind(display_gibbon.transform.Find("DEF-thigh_L"));
            display_body.leg_bottom_l.Bind(display_gibbon.transform.Find("DEF-shin_L"));
            display_body.leg_top_r.Bind(display_gibbon.transform.Find("DEF-thigh_R"));
            display_body.leg_bottom_r.Bind(display_gibbon.transform.Find("DEF-shin_R"));

            // Adjust elbow to match arm transform
            elbowTransform.position = display_body.arm_bottom_r.transform.position;

            // Set up initial IK poses (just used to get bone lengths later)
            arm_ik[0] = shoulderTransform.position;
            arm_ik[1] = elbowTransform.position;
            arm_ik[2] = gripTransform.position;

            leg_ik[0] = hipTransform.position;
            leg_ik[1] = display_body.leg_bottom_r.transform.position;
            leg_ik[2] = footTransform.position;

            var measured_arm_length = Vector3.Distance(shoulderTransform.position, elbowTransform.position) +
                                      Vector3.Distance(elbowTransform.position, gripTransform.position);

            // Set up movement system particles and bones
            for (var i = 0; i < 4; ++i)
            {
                Verlet.System new_simple_rig;
                switch (i)
                {
                    case 0:
                        new_simple_rig = displayMovementSystem.simple_rig;
                        break;
                    case 1:
                        new_simple_rig = walkMovementSystem.simple_rig;
                        break;
                    case 2:
                        new_simple_rig = jumpMovementSystem.simple_rig;
                        break;
                    default:
                        new_simple_rig = swingMovementSystem.simple_rig;
                        break;
                }

                new_simple_rig.AddPoint(shoulderTransform.position, "shoulder_r");
                new_simple_rig.AddPoint(gripTransform.position, "hand_r");
                new_simple_rig.AddPoint(
                    shoulderTransform.position + Vector3.right * (neckTransform.position[0] - shoulderTransform.position[0]) * 2f, "shoulder_l");
                new_simple_rig.AddPoint(gripTransform.position + Vector3.right * (neckTransform.position[0] - gripTransform.position[0]) * 2f,
                    "hand_l");
                new_simple_rig.AddPoint(new float3(neckTransform.position[0], hipTransform.position[1], neckTransform.position[2]), "body");
                new_simple_rig.points[p_shoulder_r].mass = 2f;
                new_simple_rig.points[p_shoulder_l].mass = 2f;
                new_simple_rig.points[p_base].mass = 4f;

                new_simple_rig.AddBone("arm_r", p_shoulder_r, p_hand_r);
                new_simple_rig.bones[new_simple_rig.bones.Count - 1].length[1] = measured_arm_length;
                new_simple_rig.bones[new_simple_rig.bones.Count - 1].length[0] *= bone_flex_value; // Allow arm to flex
                new_simple_rig.AddBone("arm_l", p_shoulder_l, p_hand_l);
                new_simple_rig.bones[new_simple_rig.bones.Count - 1].length[1] = measured_arm_length;
                new_simple_rig.bones[new_simple_rig.bones.Count - 1].length[0] *= bone_flex_value;
                new_simple_rig.AddBone("tri_top", p_shoulder_r, p_shoulder_l);
                new_simple_rig.AddBone("tri_r", p_shoulder_r, p_base);
                new_simple_rig.AddBone("tri_l", p_shoulder_l, p_base);
            }

            // Set up full-body IK particles and bones
            completeVerletSimulationSystem.AddPoint(shoulderTransform.position, "shoulder_r");
            completeVerletSimulationSystem.AddPoint(gripTransform.position, "hand_r");
            completeVerletSimulationSystem.AddPoint(shoulderTransform.position + Vector3.right * (neckTransform.position.x - shoulderTransform.position.x) * 2f,
                "shoulder_l");
            completeVerletSimulationSystem.AddPoint(gripTransform.position + Vector3.right * (neckTransform.position.x - gripTransform.position.x) * 2f, "hand_l");
            completeVerletSimulationSystem.AddPoint(new float3(neckTransform.position.x, hipTransform.position.y, neckTransform.position.z), "body");
            completeVerletSimulationSystem.AddPoint(headTransform.position, "head");
            completeVerletSimulationSystem.AddPoint(neckTransform.position, "neck");
            completeVerletSimulationSystem.AddPoint(stomachTransform.position, "stomach"); // 7
            completeVerletSimulationSystem.AddPoint(pelvisTransform.position, "hip"); // 8
            completeVerletSimulationSystem.AddPoint(groinTransform.position, "groin");
            completeVerletSimulationSystem.AddPoint(hipTransform.position, "hip_r");
            completeVerletSimulationSystem.AddPoint(footTransform.position, "foot_r");
            completeVerletSimulationSystem.AddPoint(hipTransform.position + Vector3.right * (neckTransform.position.x - hipTransform.position.x) * 2f, "hip_l");
            completeVerletSimulationSystem.AddPoint(footTransform.position + Vector3.right * (neckTransform.position.x - footTransform.position.x) * 2f, "foot_l");

            completeVerletSimulationSystem.AddBone("arm_r", p_shoulder_r, p_hand_r);
            completeVerletSimulationSystem.bones[completeVerletSimulationSystem.bones.Count - 1].length[1] = measured_arm_length;
            completeVerletSimulationSystem.bones[completeVerletSimulationSystem.bones.Count - 1].length[0] *= bone_flex_value;
            completeVerletSimulationSystem.AddBone("arm_l", p_shoulder_l, p_hand_l);
            completeVerletSimulationSystem.bones[completeVerletSimulationSystem.bones.Count - 1].length[1] = measured_arm_length;
            completeVerletSimulationSystem.bones[completeVerletSimulationSystem.bones.Count - 1].length[0] *= bone_flex_value;
            completeVerletSimulationSystem.AddBone("head", p_head, p_neck);
            completeVerletSimulationSystem.AddBone("chest", p_neck, p_stomach);
            completeVerletSimulationSystem.AddBone("belly", p_stomach, p_hip);
            completeVerletSimulationSystem.AddBone("pelvis", p_hip, p_groin);
            completeVerletSimulationSystem.AddBone("leg_r", p_hip_r, p_foot_r);
            completeVerletSimulationSystem.bones[completeVerletSimulationSystem.bones.Count - 1].length[0] *= bone_flex_value;
            completeVerletSimulationSystem.AddBone("leg_l", p_hip_l, p_foot_l);
            completeVerletSimulationSystem.bones[completeVerletSimulationSystem.bones.Count - 1].length[0] *= bone_flex_value;

            // Create random branch 'terrain'
            var num_segments = 40;
            float x = 0;
            float y = 0;
            for (var i = 0; i < num_segments + 1; ++i)
            {
                branches.AddPoint(new float3(x, y, 0), "branch");
                x += Random.Range(2.0f, 6.0f);
                y += Random.Range(-3.0f, 3.0f);
                y = math.clamp(y, -2.5f, 2.5f); // Make sure we stay on screen
            }

            for (var i = 0; i < num_segments; ++i) branches.AddBone("branch", i, i + 1);

            // Delete visible points so we don't see it when playing game
            Destroy(rootTransform.gameObject);
        }

        // Prepare to draw next frame
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                // Start jump
                simple_vel[1] = 5.0f;
                if (!b_in_air && on_branch == 0.0f) 
                    simple_vel[1] += 2.0f;
                
                b_in_air = true;

                // Copy display rig pose from previous frame to jump rig
                for (var i = 0; i < jumpMovementSystem.simple_rig.points.Count; ++i)
                {
                    jumpMovementSystem.simple_rig.points[i].pos = displayMovementSystem.simple_rig.points[i].pos;
                    jumpMovementSystem.simple_rig.points[i].old_pos = math.lerp(displayMovementSystem.simple_rig.points[i].old_pos,
                        displayMovementSystem.simple_rig.points[i].pos, 0.75f); // Adjust for different timestep
                }

                for (var i = 0; i < 2; ++i) 
                    jumpMovementSystem.simple_rig.bones[i].length[1] = displayMovementSystem.simple_rig.bones[i].length[1];

                // Initial trajectory info
                jump_time = Time.time;
                jump_point = (displayMovementSystem.limb_targets[2] + displayMovementSystem.limb_targets[3]) * 0.5f;
                predicted_land_time = jump_time + 5.0f;

                // Adjust COM
                var total_mass = 0f;
                var com = float3.zero;
                for (var i = 0; i < displayMovementSystem.simple_rig.points.Count; ++i)
                {
                    com += displayMovementSystem.simple_rig.points[i].pos * displayMovementSystem.simple_rig.points[i].mass;
                    total_mass += displayMovementSystem.simple_rig.points[i].mass;
                }

                com /= total_mass;
                jump_com_offset = com - simple_pos;
            }

            {
                // Use "arms" rig to drive full body IK rig
                var points = displayMovementSystem.simple_rig.points;

                // Calculate midpoint and orientation of body triangle
                var bind_mid = (points[0].bind_pos + points[2].bind_pos + points[4].bind_pos) / 3.0f;
                var mid = (points[0].pos + points[2].pos + points[4].pos) / 3.0f;
                var forward = math.normalize(math.cross(points[0].pos - points[2].pos, points[0].pos - points[4].pos));
                var bind_forward = math.normalize(math.cross(points[0].bind_pos - points[2].bind_pos,
                    points[0].bind_pos - points[4].bind_pos));
                var up = math.normalize((points[0].pos + points[2].pos) / 2.0f - points[4].pos);
                var bind_up = math.normalize((points[0].bind_pos + points[2].bind_pos) / 2.0f - points[4].bind_pos);

                // Copy hand and shoulder positions from simple rig
                for (var i = 0; i < 4; ++i)
                {
                    completeVerletSimulationSystem.points[i].pos = points[i].pos;
                    completeVerletSimulationSystem.points[i].pinned = true;
                }

                var body_rotation = math.mul(quaternion.LookRotation(forward, up),
                    math.inverse(quaternion.LookRotation(bind_forward, bind_up)));

                // Set up spine, head and leg positions based on body rotation
                for (var i = 5; i < 14; ++i)
                {
                    completeVerletSimulationSystem.points[i].pos = mid + math.mul(body_rotation, completeVerletSimulationSystem.points[i].bind_pos - bind_mid);
                    completeVerletSimulationSystem.points[i].pinned = true;
                }

                // Apply body compression
                completeVerletSimulationSystem.points[7].pinned = false;
                completeVerletSimulationSystem.points[8].pinned = false;
                var old_hip = completeVerletSimulationSystem.points[9].pos;
                for (var i = 7; i <= 9; ++i)
                    completeVerletSimulationSystem.points[i].pos =
                        math.lerp(completeVerletSimulationSystem.points[i].pos, completeVerletSimulationSystem.points[6].pos, body_compress_amount);
                completeVerletSimulationSystem.points[7].pos -= forward * body_compress_amount * 0.2f;
                completeVerletSimulationSystem.points[8].pos -= forward * body_compress_amount * 0.2f;

                for (var i = 10; i < 14; ++i) completeVerletSimulationSystem.points[i].pos += completeVerletSimulationSystem.points[9].pos - old_hip;

                // Move feet to foot targets
                for (var i = 0; i < 2; ++i) completeVerletSimulationSystem.points[11 + i * 2].pos = displayMovementSystem.limb_targets[2 + i];

                // Enforce bone length constraints
                for (var i = 0; i < 2; ++i) completeVerletSimulationSystem.EnforceDistanceConstraints();
            }

            {
                // Apply full body IK rig to visual deformation bones
                var points = completeVerletSimulationSystem.points;

                // Get torso orientation and position
                var bind_mid = (points[0].bind_pos + points[2].bind_pos + points[9].bind_pos) / 3.0f;
                var mid = (points[0].pos + points[2].pos + points[9].pos) / 3.0f;
                var forward = -math.normalize(math.cross(points[0].pos - points[2].pos, points[0].pos - points[9].pos));
                var bind_forward = -math.normalize(math.cross(points[0].bind_pos - points[2].bind_pos,
                    points[0].bind_pos - points[9].bind_pos));
                var up = math.normalize((points[0].pos + points[2].pos) / 2.0f - points[9].pos);
                var bind_up = math.normalize((points[0].bind_pos + points[2].bind_pos) / 2.0f - points[9].bind_pos);

                // Apply core bones
                ApplyBound(display_body.head, forward, bind_forward, 5, 6);
                ApplyBound(display_body.chest, forward, bind_forward, 6, 7);
                ApplyBound(display_body.belly, forward, bind_forward, 7, 8);
                ApplyBound(display_body.pelvis, forward, bind_forward, 8, 9);

                // Arm IK
                for (var i = 0; i < 2; ++i)
                {
                    var top = display_body.arm_top_r;
                    var bottom = display_body.arm_bottom_r;
                    if (i == 1)
                    {
                        top = display_body.arm_top_l;
                        bottom = display_body.arm_bottom_l;
                    }

                    var start_id = i * 2;
                    var end_id = i * 2 + 1;
                    var start = points[start_id];
                    var end = points[end_id];

                    // Adjust elbow target position
                    var ik_driver = math.max(on_branch, in_air_amount);
                    var ik_forward_amount = -ik_driver * 0.8f;
                    var ik_up_amount = 0.1f + ik_driver * 0.5f;
                    var elbow_point = (points[2].pos + points[0].pos) * 0.5f + up * ik_up_amount +
                                      forward * ik_forward_amount;
                    var bind_elbow_point = (points[2].bind_pos + points[0].bind_pos) * 0.5f + bind_up * ik_up_amount +
                                           bind_forward * ik_forward_amount;

                    if (debug_info.draw_elbow_ik_target)
                    {
                        DebugDraw.Line((start.pos + end.pos) * 0.5f, elbow_point, Color.red,
                            DebugDraw.Lifetime.OneFrame, DebugDraw.Type.Xray);
                        DebugDraw.Sphere(elbow_point, Color.red, Vector3.one * 0.1f, Quaternion.identity,
                            DebugDraw.Lifetime.OneFrame, DebugDraw.Type.Xray);
                    }

                    var old_axis = math.normalize(math.cross((end.bind_pos + start.bind_pos) * 0.5f - bind_elbow_point,
                        start.bind_pos - end.bind_pos));
                    var axis = math.normalize(math.cross((end.pos + start.pos) * 0.5f - elbow_point,
                        start.pos - end.pos));

                    ApplyTwoBoneIK(start_id, end_id, forward, arm_ik, top, bottom, completeVerletSimulationSystem.points, old_axis, axis);

                    if (debug_info.draw_ik_final)
                    {
                        DebugDraw.Line(points[start_id].pos, bottom.transform.position, Color.white,
                            DebugDraw.Lifetime.OneFrame, DebugDraw.Type.Xray);
                        DebugDraw.Line(points[end_id].pos, bottom.transform.position, Color.white,
                            DebugDraw.Lifetime.OneFrame, DebugDraw.Type.Xray);
                    }
                }

                // Leg IK
                for (var i = 0; i < 2; ++i)
                {
                    var top = display_body.leg_top_r;
                    var bottom = display_body.leg_bottom_r;
                    if (i == 1)
                    {
                        top = display_body.leg_top_l;
                        bottom = display_body.leg_bottom_l;
                    }

                    var start = i * 2 + 10;
                    var end = i * 2 + 1 + 10;

                    var leg_dir = points[end].pos - points[start].pos;

                    // Get knee direction
                    var leg_dir_flat = math.normalize(new float2(math.dot(leg_dir, forward), math.dot(leg_dir, up)));
                    var leg_forward = leg_dir_flat[0] * up + leg_dir_flat[1] * -forward;

                    // Get base whole-leg rotation
                    var bind_rotation =
                        Quaternion.LookRotation(points[end].bind_pos - points[start].bind_pos, Vector3.forward);
                    var rotation = Quaternion.LookRotation(leg_dir, leg_forward) * bind_rotation;

                    // Get knee bend axis
                    var old_axis = bind_rotation * Vector3.right;
                    var axis = rotation * Vector3.right;

                    ApplyTwoBoneIK(start, end, leg_forward, leg_ik, top, bottom, completeVerletSimulationSystem.points, old_axis, axis);

                    if (debug_info.draw_ik_final)
                    {
                        DebugDraw.Line(points[start].pos, bottom.transform.position, Color.white,
                            DebugDraw.Lifetime.OneFrame, DebugDraw.Type.Xray);
                        DebugDraw.Line(points[end].pos, bottom.transform.position, Color.white,
                            DebugDraw.Lifetime.OneFrame, DebugDraw.Type.Xray);
                    }
                }

                // Head look            
                // head_look_y: 50 = max look down, -70 = max look up
                // head_look_x: -90 to 90

                // Get head target in head transform space
                var target = math.normalize(display_body.head.transform.InverseTransformPoint(look_target));
                // Using sin here is not correct (should be asin or something), but looks ok so keeping it for now
                var head_look_y = math.sin(target.x) * Mathf.Rad2Deg;
                // Flatten look direction to solve other rotation axis
                var temp = target;
                temp.x = 0.0f;
                temp = math.normalize(temp);
                var head_look_x = -math.sin(temp.y) * Mathf.Rad2Deg;

                // Apply head transform
                display_body.head.transform.rotation = display_body.head.transform.rotation *
                                                       Quaternion.Euler(head_look_x, 0f, 0f) *
                                                       Quaternion.Euler(0f, head_look_y, 0f);
                if (head_look_y > 0.0f)
                    display_body.head.transform.position = display_body.head.transform.position +
                                                           display_body.head.transform.right * head_look_y * -0.001f;

                if (debug_info.draw_head_look)
                {
                    DebugDraw.Sphere(look_target, Color.red, Vector3.one * 0.1f, Quaternion.identity,
                        DebugDraw.Lifetime.OneFrame, DebugDraw.Type.Xray);
                    DebugDraw.Line(display_body.head.transform.position, look_target, Color.red,
                        DebugDraw.Lifetime.OneFrame, DebugDraw.Type.Xray);
                }
            }

            // Debug draw skeleton
            branches.DrawBones(new Color(0.5f, 0.5f, 0.1f, 1.0f));
            if (debug_info.draw_walk_rig && in_air_amount < 1.0f && on_branch > 0.0f) DrawSystem(walkMovementSystem, Color.red);
            if (debug_info.draw_swing_rig && in_air_amount < 1.0f && on_branch < 1.0f) DrawSystem(swingMovementSystem, Color.cyan);
            if (debug_info.draw_jump_rig && in_air_amount > 0.0f) DrawSystem(jumpMovementSystem, Color.green);
            if (debug_info.draw_display_simple_rig) DrawSystem(displayMovementSystem, Color.white);
            if (debug_info.draw_display_complete_rig) completeVerletSimulationSystem.DrawBones(Color.white);
            if (debug_info.draw_ik_final)
            {
                for (var i = 2; i < completeVerletSimulationSystem.bones.Count - 2; ++i)
                    DebugDraw.Line(completeVerletSimulationSystem.points[completeVerletSimulationSystem.bones[i].points[0]].pos,
                        completeVerletSimulationSystem.points[completeVerletSimulationSystem.bones[i].points[1]].pos, Color.white, DebugDraw.Lifetime.OneFrame,
                        DebugDraw.Type.Xray);
                DebugDraw.Line(completeVerletSimulationSystem.points[0].pos, completeVerletSimulationSystem.points[2].pos, Color.white, DebugDraw.Lifetime.OneFrame,
                    DebugDraw.Type.Xray);
                DebugDraw.Line(completeVerletSimulationSystem.points[10].pos, completeVerletSimulationSystem.points[12].pos, Color.white,
                    DebugDraw.Lifetime.OneFrame, DebugDraw.Type.Xray);
            }

            display_gibbon.SetActive(debug_info.draw_gibbon);

            /*
            if(ImGui.Begin("Animation Tuning")){
                ImGui.TextWrapped("This is an example of a temporary UI window that could be used to tune animation parameters without having to recompile");
                ImGui.SliderFloat("gallop_offset", ref gallop_offset, -1f, 1f);
                ImGui.SliderFloat("gallop_stride", ref gallop_stride, 0f, 0.85f);
                ImGui.SliderFloat("gallop_height_offset", ref gallop_height_offset, 0f, 1f);
                ImGui.SliderFloat("gallop_height", ref gallop_height, 0f, 1f);
                ImGui.SliderFloat("gallop_height_base", ref gallop_height_base, 0f, 1f);
                ImGui.SliderFloat("gallop_hip_rotate", ref gallop_hip_rotate, -4f, 4f);
                ImGui.SliderFloat("gallop_lean", ref gallop_lean, -4f, 4f);
                ImGui.SliderFloat("gallop_stride_height", ref gallop_stride_height, -1f, 1f);
                ImGui.SliderFloat("gallop_arm_stride", ref gallop_arm_stride, 0f, 4f);
                ImGui.SliderFloat("gallop_arm_stride_height", ref gallop_arm_stride_height, 0f, 1f);
                ImGui.SliderFloat("quad_amount", ref quad_amount, 0f, 1f);
                ImGui.SliderFloat("quad_gallop_body_compress_amount", ref quad_gallop_body_compress_amount, 0f, 1f);
                ImGui.SliderFloat("quad_gallop_body_compress_offset", ref quad_gallop_body_compress_offset, 0f, 1f);
                ImGui.SliderFloat("gallop_amount", ref gallop_amount, 0f, 1f);
            }
            ImGui.End();
            
            */

            debug_info.DrawWindow();

            if (Input.GetKeyDown(KeyCode.Tab)) Time.timeScale = Time.timeScale == 1.0f ? 0.1f : 1.0f;
        }

        private void FixedUpdate()
        {
            Step(Time.fixedDeltaTime);
        }

        // Use law of cosines to find angles of triangle
        private static float GetAngleGivenSides(float a, float b, float c)
        {
            var top = c * c - a * a - b * b;
            var divisor = -2 * a * b;
            if (divisor == 0f) return 0f;
            return math.acos(math.clamp(top / divisor, -1f, 1f));
        }

        // Solve two bone IK problems
        private static void ApplyTwoBoneIK(int start_id,
            int end_id,
            float3 forward,
            float3[] ik,
            DisplayBone top,
            DisplayBone bottom,
            List<Verlet.Point> points,
            float3 old_axis,
            float3 axis)
        {
            var start = points[start_id];
            var end = points[end_id];

            // Get sides of triangle formed by upper and lower limb
            var dist_a = math.distance(ik[0], ik[1]);
            var dist_b = math.distance(ik[1], ik[2]);
            var dist_c = math.distance(start.pos, end.pos);
            var old_dist_c = math.distance(ik[0], ik[2]);

            // Get angles of triangle
            var old_hinge_angle = GetAngleGivenSides(dist_a, dist_b, old_dist_c);
            var hinge_angle = GetAngleGivenSides(dist_a, dist_b, dist_c);
            var old_base_angle = GetAngleGivenSides(old_dist_c, dist_a, dist_b);
            var base_angle = GetAngleGivenSides(dist_c, dist_a, dist_b);

            // Apply rotation of entire arm (shoulder->hand)
            var base_rotation = Quaternion.LookRotation(end.pos - start.pos, forward) *
                                Quaternion.Inverse(Quaternion.LookRotation(end.bind_pos - start.bind_pos,
                                    Vector3.forward));
            // Apply additional rotation from IK
            base_rotation = Quaternion.AngleAxis(base_angle * Mathf.Rad2Deg, axis) * base_rotation *
                            Quaternion.Inverse(Quaternion.AngleAxis(old_base_angle * Mathf.Rad2Deg, old_axis));

            // Apply base and hinge rotations to actual display bones
            top.transform.position = top.bind_pos + (start.pos - start.bind_pos);
            top.transform.rotation = base_rotation * top.bind_rot;

            bottom.transform.position = top.transform.position + top.transform.rotation *
                Quaternion.Inverse(top.bind_rot) * (bottom.bind_pos - top.bind_pos);
            bottom.transform.rotation = Quaternion.AngleAxis(hinge_angle * Mathf.Rad2Deg, axis) * base_rotation *
                                        Quaternion.Inverse(Quaternion.AngleAxis(old_hinge_angle * Mathf.Rad2Deg,
                                            old_axis)) * bottom.bind_rot;
        }

        // Calculate bone transform that matches orientation of top and bottom points, and looks in the character "forward" direction
        private void ApplyBound(DisplayBone part, float3 forward, float3 bind_forward, int start, int end)
        {
            // Get midpoint and "up" direction (from start to end point)
            var up = math.normalize(completeVerletSimulationSystem.points[end].pos - completeVerletSimulationSystem.points[start].pos);
            var bind_up = math.normalize(completeVerletSimulationSystem.points[end].bind_pos - completeVerletSimulationSystem.points[start].bind_pos);
            var mid = (completeVerletSimulationSystem.points[end].pos + completeVerletSimulationSystem.points[start].pos) / 2.0f;
            var bind_mid = (completeVerletSimulationSystem.points[end].bind_pos + completeVerletSimulationSystem.points[start].bind_pos) / 2.0f;

            // Apply rotations
            var rotation = Quaternion.LookRotation(up, forward) *
                           Quaternion.Inverse(Quaternion.LookRotation(bind_up, bind_forward));
            part.transform.rotation = rotation * part.bind_rot;
            part.transform.position = mid + (float3)(rotation * (part.bind_pos - bind_mid));
        }

        // Get height of branch at given x coordinate
        private float BranchHeight(float x, int start_id, int end_id)
        {
            var start = branches.points[start_id];
            var end = branches.points[end_id];
            var branch_t = (x - start.bind_pos[0]) / (end.bind_pos[0] - start.bind_pos[0]);
            return math.lerp(start.pos[1], end.pos[1], branch_t);
        }

        // Get height of entire branch terrain at given x coordinate
        private float BranchesHeight(float x)
        {
            for (var i = 0; i < branches.bones.Count; ++i)
            {
                var point_ids = branches.bones[i].points;
                if (x >= branches.points[point_ids[0]].pos[0] && x < branches.points[point_ids[1]].pos[0])
                    return BranchHeight(x, point_ids[0], point_ids[1]);
            }

            // If not on terrain, extend horizontally forever
            if (x < 0.0f)
                return branches.points[0].pos[1];
            return branches.points[branches.points.Count - 1].pos[1];
        }

        private static void DrawSystem(MovementSystem system, Color color)
        {
            system.simple_rig.DrawBones(color);
            for (var i = 2; i < 4; ++i)
                DebugDraw.Sphere(system.limb_targets[i], color, Vector3.one * 0.1f, Quaternion.identity,
                    DebugDraw.Lifetime.OneFrame, DebugDraw.Type.Xray);
        }

        private float3 MoveTowards(float3 a, float3 b, float max_dist)
        {
            var len = math.distance(a, b);
            if (len < max_dist)
                return b;
            return a + (b - a) / len * max_dist;
        }

        private void Swap(ref float3 a, ref float3 b)
        {
            var temp = a;
            a = b;
            b = temp;
        }

        private void PreventHandsFromCrossingBody(Verlet.System rig)
        {
            for (var i = 0; i < 2; ++i)
            {
                var side_dir = math.normalize(rig.points[0].pos - rig.points[2].pos) * (1 - i * 2);
                var shoulder_d = math.dot(rig.points[i * 2].pos, side_dir);
                var hand_d = math.dot(rig.points[i * 2 + 1].pos, side_dir);
                var new_d = math.max(hand_d, shoulder_d);
                rig.points[i * 2 + 1].pos += (new_d - hand_d) * side_dir;
            }
        }

        // Apply actual controls and physics
        private void Step(float step)
        {
            // Transform controls to axes
            var horz_input = 0f;
            var vert_input = 0f;
            if (Input.GetKey(KeyCode.D)) horz_input = 1f;
            if (Input.GetKey(KeyCode.A)) horz_input = -1f;
            if (Input.GetKey(KeyCode.W)) vert_input = 1f;
            if (Input.GetKey(KeyCode.S))
            {
                vert_input = -1f;
                if (on_branch == 1.0f)
                {
                    // Fix timing and handholds for transition from walk->swing
                    swing_time = 0.0f;
                    for (var i = 0; i < 2; ++i) swingMovementSystem.limb_targets[i] = simple_pos;
                }
            }

            // If not jumping, can use up and down input to transition between swinging and running
            if (!b_in_air)
            {
                if (vert_input < 0f)
                    b_wants_to_swing = true;
                else if (vert_input > 0f) b_wants_to_swing = false;
            }

            // Transition between swinging and running
            on_branch = Mathf.MoveTowards(on_branch, b_wants_to_swing ? 0.0f : 1.0f, step * 2f);

            // Max speed of 10 m/s while swinging or 7 m/s while running
            var max_speed = 10f - on_branch * 3.0f;

            if (!b_in_air) // Player can influence horizontal velocity if not jumping
                simple_vel[0] += horz_input * step * 5f;
            simple_vel[0] = math.clamp(simple_vel[0], -max_speed, max_speed);

            // Don't allow speed < 1.0 m/s, don't need to worry about idle animations in an endless runner
            if (horz_input == 0f && math.abs(simple_vel[0]) < 1.0f)
                simple_vel[0] = Mathf.MoveTowards(simple_vel[0], simple_vel[0] >= 0.0f ? 1.0f : -1.0f, step);

            // Smooth out position on branch by checking height forwards and back
            var future_pos = simple_pos + simple_vel * 0.1f;
            future_pos[1] = BranchesHeight(future_pos[0]);
            var past_pos = simple_pos + simple_vel * -0.1f;
            past_pos[1] = BranchesHeight(past_pos[0]);
            var smoothed_pos = (future_pos + past_pos + simple_pos) / 3.0f;

            // Get slope and use it to modify running speed
            var slope_vec = math.normalizesafe(future_pos - simple_pos);
            var slope_speed_mult = math.abs(slope_vec[0]);
            if (on_branch < 0.5f || b_in_air) slope_speed_mult = 1.0f;

            // Apply modified running speed to position
            var effective_vel = simple_vel * slope_speed_mult;
            simple_pos += effective_vel * step;

            if (b_in_air)
            {
                // If jumping, predict landing time and position
                jump_com_offset *= 0.99f;
                simple_vel += (float3)Physics.gravity * step;

                if (debug_info.draw_trajectory)
                    DebugDraw.Sphere(jump_point, Color.green, Vector3.one * 0.4f, Quaternion.identity,
                        DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);

                // Simulate time steps in future to predict landing
                var traj_vel = simple_vel;
                var traj_pos = simple_pos;
                for (var i = 0; i < 200; ++i)
                {
                    traj_pos += traj_vel * step;
                    traj_vel += (float3)Physics.gravity * step;
                    if (debug_info.draw_trajectory)
                        DebugDraw.Sphere(traj_pos, new Color(0.0f, 1.0f, 0.0f, 0.1f), Vector3.one * 0.1f,
                            Quaternion.identity, DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);
                    predicted_land_point = traj_pos;
                    if (traj_vel[1] <= 0.0f && traj_pos[1] < BranchesHeight(traj_pos[0]))
                    {
                        predicted_land_time = Time.time + step * i;
                        predicted_land_point[1] = BranchesHeight(predicted_land_point[0]);
                        if (debug_info.draw_trajectory)
                            DebugDraw.Sphere(predicted_land_point, Color.green, Vector3.one * 0.4f, Quaternion.identity,
                                DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);
                        break;
                    }
                }

                // Check if foot is below terrain, if so, land
                var test_point = displayMovementSystem.limb_targets[2] + simple_vel * step;
                if (simple_vel[1] <= 0.0f && test_point[1] < BranchesHeight(test_point[0]))
                {
                    b_in_air = false;
                    b_wants_to_swing = true;
                    on_branch = 1.0f;
                    swing_time = 0.0f;
                    for (var i = 0; i < displayMovementSystem.simple_rig.points.Count; ++i)
                        walkMovementSystem.simple_rig.points[i].pos = jumpMovementSystem.simple_rig.points[i].pos;
                }

                // If in air, look towards landing spot
                look_target = predicted_land_point;
            }

            if (!b_in_air)
            {
                simple_pos[1] = BranchesHeight(simple_pos[0]);
                simple_vel[1] = 0.0f;

                // If on ground, look in the direction you are moving
                var forward = math.normalize(math.cross(
                    displayMovementSystem.simple_rig.points[0].pos - displayMovementSystem.simple_rig.points[2].pos,
                    displayMovementSystem.simple_rig.points[0].pos - displayMovementSystem.simple_rig.points[4].pos));
                look_target[2] += forward[2];
                look_target = (float3)display_body.head.transform.position + forward * 0.1f;
                look_target += future_pos - past_pos;
            }

            // Could be used to transition over multiple frames, but we're jumping immediately
            in_air_amount = Mathf.MoveTowards(in_air_amount, b_in_air ? 1.0f : 0.0f, 1f);

            if (debug_info.draw_smoothing && in_air_amount < 1.0f && on_branch > 0.0f)
            {
                DebugDraw.Sphere(future_pos, Color.blue, Vector3.one * 0.1f, Quaternion.identity,
                    DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);
                DebugDraw.Sphere(past_pos, Color.blue, Vector3.one * 0.1f, Quaternion.identity,
                    DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);
                DebugDraw.Sphere(smoothed_pos, Color.green, Vector3.one * 0.2f, Quaternion.identity,
                    DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);
                DebugDraw.Line(future_pos, smoothed_pos, Color.green, DebugDraw.Lifetime.OneFixedUpdate,
                    DebugDraw.Type.Xray);
                DebugDraw.Line(past_pos, smoothed_pos, Color.green, DebugDraw.Lifetime.OneFixedUpdate,
                    DebugDraw.Type.Xray);
            }

            if (on_branch < 1.0f && in_air_amount < 1.0f)
            {
                // Swing animation
                // Adjust amplitude and time scale of height curve based on speed
                var amplitude = math.pow(math.abs(simple_vel[0]) / 10f + 1f, 0.8f) - 1f + 0.1f;
                var min_height = -1f + amplitude * 0.25f + math.max(0.0f, 0.1f - math.abs(simple_vel[0]) * 0.1f);

                // Increment swing time
                var swing_speed_mult = 8f / (math.PI * 2f);
                var old_swing_time = swing_time;
                swing_time += step * swing_speed_mult;

                var next_hand =
                    (int)swing_time %
                    2; // Swing time is measured in terms of number of hand changes, so we can use it to tell which hand is going to grab next

                // Figure out target hand positions
                var next_trough_time = math.ceil(swing_time) - 0.25f;
                swingMovementSystem.limb_targets[next_hand] =
                    simple_pos + simple_vel * (next_trough_time - swing_time) / swing_speed_mult;
                for (var i = 0; i < 2; ++i) swingMovementSystem.limb_targets[i][1] = BranchesHeight(swingMovementSystem.limb_targets[i][0]);

                var pendulum_length = 0.9f;
                swingMovementSystem.target_com = simple_pos - simple_vel * 0.05f;

                // Adjust forwards and back at slow speed to make it look more like continuous contact swinging
                swingMovementSystem.target_com[0] += math.cos((swing_time - 0.1f) * (math.PI * 2f)) * pendulum_length * 0.5f *
                                       math.clamp(simple_vel[0] * 0.5f, -1f, 1f) *
                                       math.max(0f, 1f - math.abs(simple_vel[0]) * 2f);

                {
                    // Smooth out motion so COM follows line between handholds instead of branch itself
                    // e.g. movement should be horizontal when swinging from one tip of a V shape to the next
                    // Get handhold points, including next predicted one
                    var points = new float3[3];
                    points[0] = swingMovementSystem.limb_targets[0];
                    points[1] = swingMovementSystem.limb_targets[1];
                    points[2] = swingMovementSystem.limb_targets[next_hand] + simple_vel / swing_speed_mult;
                    points[2][1] = BranchesHeight(points[2][0]);

                    // Sort points on x axis
                    if (points[0][0] > points[1][0]) Swap(ref points[0], ref points[1]);
                    if (points[1][0] > points[2][0]) Swap(ref points[1], ref points[2]);
                    if (points[0][0] > points[1][0]) Swap(ref points[0], ref points[1]);

                    // Interpolate between nearest points
                    if (swingMovementSystem.target_com[0] < points[1][0])
                    {
                        var interp = math.max(0, (swingMovementSystem.target_com[0] - points[0][0]) / (points[1][0] - points[0][0]));
                        swingMovementSystem.target_com[1] = math.lerp(points[0][1], points[1][1], interp);
                    }
                    else
                    {
                        var interp = math.min(1, (swingMovementSystem.target_com[0] - points[1][0]) / (points[2][0] - points[1][0]));
                        swingMovementSystem.target_com[1] = math.lerp(points[1][1], points[2][1], interp);
                    }

                    if (debug_info.draw_smoothing)
                    {
                        var col = Color.cyan;
                        col.a = 0.2f;
                        for (var i = 0; i < 3; ++i)
                            DebugDraw.Line(points[i] - new float3(0, 10, 0), points[i] + new float3(0, 10, 0), col,
                                DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);
                        col.a = 1.0f;
                        DebugDraw.Line(points[0], points[1], col, DebugDraw.Lifetime.OneFixedUpdate,
                            DebugDraw.Type.Xray);
                        DebugDraw.Line(points[1], points[2], col, DebugDraw.Lifetime.OneFixedUpdate,
                            DebugDraw.Type.Xray);
                        DebugDraw.Sphere(swingMovementSystem.target_com, col, Vector3.one * 0.1f, Quaternion.identity,
                            DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);
                    }
                }

                // Adjust vertical position on sin wave
                swingMovementSystem.target_com[1] +=
                    (min_height + (math.sin((swing_time - 0.1f) * (math.PI * 2f)) + 1f) * amplitude) * pendulum_length;

                // Move upwards if we're climbing onto branch for cleaner transition
                var pull_up = on_branch;
                swingMovementSystem.target_com[1] += pull_up;

                // Use COM and hand positions to drive arm rig
                // Pull hands towards grip targets
                var rig = swingMovementSystem.simple_rig;
                for (var i = 0; i < 2; ++i)
                {
                    var pull_strength = math.max(0f,
                        math.cos((swing_time + 0.35f + (1 - i)) * math.PI * 1f) * 0.5f + 0.5f);
                    if (debug_info.draw_hand_pull)
                    {
                        DebugDraw.Line(rig.points[i * 2 + 1].pos, swingMovementSystem.limb_targets[i],
                            new Color(i == 0 ? 1f : 0f, i == 0 ? 0f : 1f, 0f, pull_strength),
                            DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);
                        DebugDraw.Sphere(swingMovementSystem.limb_targets[i], new Color(i == 0 ? 1f : 0f, i == 0 ? 0f : 1f, 0f, 1.0f),
                            Vector3.one * 0.1f, Quaternion.identity, DebugDraw.Lifetime.OneFixedUpdate,
                            DebugDraw.Type.Xray);
                    }

                    rig.points[i * 2 + 1].pos = MoveTowards(rig.points[i * 2 + 1].pos, swingMovementSystem.limb_targets[i],
                        pull_strength * step * 5f);
                    rig.points[i * 2 + 1].old_pos = math.lerp(rig.points[i * 2 + 1].old_pos,
                        rig.points[i * 2 + 1].pos - simple_vel * step, 0.25f);
                }

                rig.StartSim(step);
                for (var j = 0; j < 4; ++j)
                {
                    // Adjust all free points to match target COM
                    var total_mass = 0f;
                    var com = float3.zero;
                    for (var i = 0; i < rig.points.Count; ++i)
                        if (rig.points[i].pinned == false)
                        {
                            com += rig.points[i].pos * rig.points[i].mass;
                            total_mass += rig.points[i].mass;
                        }

                    com /= total_mass;
                    var offset = swingMovementSystem.target_com - com;
                    for (var i = 0; i < rig.points.Count; ++i)
                        if (i != 1 && i != 3)
                            rig.points[i].pos += offset;
                    // Apply torque to keep torso upright and forward-facing
                    var step_sqrd = step * step;
                    var force = 20f;
                    rig.points[4].pos[1] -= step_sqrd * force;
                    rig.points[0].pos[1] += step_sqrd * force * 0.5f;
                    rig.points[2].pos[1] += step_sqrd * force * 0.5f;
                    rig.points[0].pos[2] -= step_sqrd * simple_vel[0] * 2.0f;
                    rig.points[2].pos[2] += step_sqrd * simple_vel[0] * 2.0f;
                    rig.points[4].pos[0] -=
                        simple_vel[0] * step_sqrd * 2f; // Apply backwards force to maintain forwards tilt

                    rig.EnforceDistanceConstraints();
                }

                rig.EndSim();

                var up = math.normalize((rig.points[0].pos + rig.points[2].pos) * 0.5f - rig.points[4].pos);
                for (var i = 0; i < 2; ++i)
                    swingMovementSystem.limb_targets[2 + i] = rig.points[i * 2].pos - up +
                                                up * (0.35f + 0.15f * math.sin((swing_time + i * 1.0f) * math.PI));
            }

            if (in_air_amount > 0.0f)
            {
                // Jump animation
                jumpMovementSystem.target_com = simple_pos + jump_com_offset;

                // Calculate a value that is 0 on launch and landing, and approaches 1 towards the peak
                var dist_from_land = 1.0f - 1.0f / (1.0f +
                                                    math.max(0.0f,
                                                        math.min(Time.time - jump_time,
                                                            predicted_land_time - Time.time)) * 4.0f);

                // Compress body at peak
                jumpMovementSystem.body_compress_amount = dist_from_land * 0.1f;

                var rig = jumpMovementSystem.simple_rig;

                // Add some drag
                for (var i = 0; i < rig.points.Count; ++i)
                    rig.points[i].old_pos = math.lerp(rig.points[i].old_pos,
                        rig.points[i].pos - simple_vel * step * 0.25f, 0.03f);

                rig.StartSim(step);
                for (var j = 0; j < 4; ++j)
                {
                    // Adjust all free points to match target COM
                    var total_mass = 0f;
                    var com = float3.zero;
                    for (var i = 0; i < rig.points.Count; ++i)
                    {
                        com += rig.points[i].pos * rig.points[i].mass;
                        total_mass += rig.points[i].mass;
                    }

                    com /= total_mass;
                    var offset = jumpMovementSystem.target_com - com;
                    for (var i = 0; i < rig.points.Count; ++i) rig.points[i].pos += offset;

                    // Interpolate from launch point to land point based on time
                    var dir = math.lerp(math.normalize(jump_point - jumpMovementSystem.target_com),
                        math.normalize(predicted_land_point - jumpMovementSystem.target_com),
                        (Time.time - jump_time) / (predicted_land_time - jump_time));
                    dir[0] = math.clamp(dir[0], -0.3f, 0.3f);
                    dir[1] = math.clamp(dir[1], -0.7f, -1.0f);
                    if (debug_info.draw_trajectory)
                        DebugDraw.Line(rig.points[4].pos, rig.points[4].pos + dir, Color.green,
                            DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);

                    // Apply forces to orient character's feet towards launch/land sites
                    var step_sqrd = step * step;
                    var force = 10f;
                    rig.points[4].pos += dir * step_sqrd * force;
                    rig.points[0].pos -= dir * step_sqrd * force * 0.5f;
                    rig.points[2].pos -= dir * step_sqrd * force * 0.5f;

                    // Encourage character to face in direction of velocity
                    rig.points[0].pos[2] -= step_sqrd * simple_vel[0] * 0.4f;
                    rig.points[2].pos[2] += step_sqrd * simple_vel[0] * 0.4f;

                    // Bend arms a bit
                    for (var i = 0; i < 2; ++i)
                        rig.bones[i].length[1] = Mathf.MoveTowards(rig.bones[i].length[1],
                            rig.bones[i].length[0] / 0.4f * 0.8f, step * 0.2f);

                    PreventHandsFromCrossingBody(rig);

                    rig.EnforceDistanceConstraints();
                }

                rig.EndSim();

                // Contract legs towards peak of jump
                var up = math.normalize((rig.points[0].pos + rig.points[2].pos) * 0.5f - rig.points[4].pos);
                var forward = math.normalize(math.cross(rig.points[0].pos - rig.points[2].pos,
                    rig.points[0].pos - rig.points[4].pos));
                for (var i = 0; i < 2; ++i)
                    jumpMovementSystem.limb_targets[2 + i] =
                        rig.points[i * 2].pos - up + (up * 0.5f + forward * 0.1f) * dist_from_land;
            }

            {
                // Run animation
                // Vary between different gaits based on speed and time
                gallop_amount = math.clamp(math.abs(simple_vel[0]) / 4f + (math.sin(Time.time * 0.7f) - 1.0f) * 0.7f,
                    0.0f, 1.0f);
                quad_amount = math.clamp(math.sin(Time.time * 2.3f) + math.sin(Time.time * 1.7f), 0.0f, 1.0f);

                if (debug_info.force_run) gallop_amount = 0.0f;
                if (debug_info.force_gallop)
                {
                    gallop_amount = 1.0f;
                    quad_amount = 0.0f;
                }

                if (debug_info.force_quad)
                {
                    gallop_amount = 1.0f;
                    quad_amount = 1.0f;
                }

                // Determine how far to lean forwards
                var walk_lean = math.sin(Time.time) * 0.2f + 0.3f;
                var gallop_lean = math.sin(Time.time) * 0.2f + 0.8f + quad_amount * 0.07f * math.abs(effective_vel[0]);
                var lean = math.lerp(walk_lean, gallop_lean, gallop_amount);

                // Adjust stride frequency based on speed
                var speed_mult = 8f / (math.PI * 2f) * math.pow(math.abs(effective_vel[0]) + 1.0f, 0.4f);
                walk_time += step * speed_mult;

                // Start sliding if above threshold speed and slope 
                var target_skate_amount = 0.0f;
                if (slope_vec[1] < -0.5f && math.abs(effective_vel[0]) > 3.0f) target_skate_amount = 1.0f;
                skate_amount = Mathf.MoveTowards(skate_amount, target_skate_amount, step * 3.0f);
                if (debug_info.force_skate) skate_amount = 1.0f;
                quad_amount = math.lerp(quad_amount, 0.0f, skate_amount);

                // Compress body during quadruped gallop
                walkMovementSystem.body_compress_amount = math.lerp(
                    (math.sin((walk_time + quad_gallop_body_compress_offset) * math.PI * 2.0f) + 1.0f) *
                    quad_gallop_body_compress_amount * quad_amount * gallop_amount,
                    0.1f,
                    skate_amount);

                // Adjust COM height based on gait
                var target_com = simple_pos;
                target_com[1] = smoothed_pos[1];
                var walk_height = base_walk_height +
                                  math.sin((walk_time + 0.25f) * math.PI * 4.0f) * math.abs(effective_vel[0]) * 0.015f /
                                  speed_mult + math.abs(effective_vel[0]) * 0.01f;
                var gallop_height_ =
                    math.sin((walk_time + gallop_height_offset) * math.PI * 2.0f) * gallop_height *
                    math.abs(effective_vel[0]) +
                    gallop_height_base * (0.5f + math.min(0.5f, math.abs(effective_vel[0]) * 0.1f));
                target_com[1] += math.lerp(walk_height, gallop_height_, gallop_amount);
                target_com[1] = math.lerp(target_com[1], simple_pos[1] + 0.5f, skate_amount);
                target_com[1] = math.lerp(target_com[1], simple_pos[1], math.abs(lean) * 0.15f);
                if (!b_wants_to_swing)
                    target_com[1] = math.lerp(target_com[1], smoothed_pos[1], (1.0f - on_branch) * 0.7f);

                // Get ground slope again for use later
                var left = simple_pos - new float3(0.1f, 0.0f, 0.0f);
                var right = simple_pos + new float3(0.1f, 0.0f, 0.0f);
                left[1] = BranchesHeight(left[0]);
                right[1] = BranchesHeight(right[0]);
                var move_dir = math.normalize(right - left);

                if (b_in_air)
                {
                    walkMovementSystem.simple_rig.StartSim(step);
                    for (var i = 0; i < displayMovementSystem.simple_rig.points.Count; ++i)
                        walkMovementSystem.simple_rig.points[i].pos = displayMovementSystem.simple_rig.points[i].pos;
                    walkMovementSystem.simple_rig.EndSim();
                }
                else
                {
                    var rig = walkMovementSystem.simple_rig;

                    // Apply quadruped run effect to arms
                    if (gallop_amount * quad_amount > 0.0f)
                        for (var i = 0; i < 2; ++i)
                        {
                            // Get target position for hand
                            walkMovementSystem.limb_targets[i] = rig.points[i * 2].pos;
                            walkMovementSystem.limb_targets[i][1] = BranchesHeight(displayMovementSystem.limb_targets[i][0]);
                            var time_val = walk_time * math.PI * 2.0f;
                            walkMovementSystem.limb_targets[i][1] += (-math.sin(time_val) + 0.5f) * gallop_arm_stride_height;
                            walkMovementSystem.limb_targets[i] += move_dir * (math.cos(time_val) + 0.5f) * gallop_arm_stride *
                                effective_vel[0] / speed_mult;

                            // Move hand towards target
                            var pull_strength = gallop_amount * quad_amount *
                                                math.min(1.0f, math.abs(effective_vel[0]) * 0.2f);
                            rig.points[i * 2 + 1].pos = MoveTowards(rig.points[i * 2 + 1].pos, walkMovementSystem.limb_targets[i],
                                step * 0.5f * pull_strength * 4f);

                            if (on_branch > 0.0f && in_air_amount < 1.0f && debug_info.draw_hand_pull)
                            {
                                DebugDraw.Line(rig.points[i * 2 + 1].pos, walkMovementSystem.limb_targets[i],
                                    new Color(i == 0 ? 1f : 0f, i == 0 ? 0f : 1f, 0f, pull_strength),
                                    DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);
                                DebugDraw.Sphere(walkMovementSystem.limb_targets[i],
                                    new Color(i == 0 ? 1f : 0f, i == 0 ? 0f : 1f, 0f, 1.0f), Vector3.one * 0.1f,
                                    Quaternion.identity, DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);
                            }
                        }

                    rig.StartSim(step);
                    for (var j = 0; j < 4; ++j)
                    {
                        // Adjust all free points to match target COM
                        var total_mass = 0f;
                        var com = float3.zero;
                        for (var i = 0; i < rig.points.Count; ++i)
                            if (i != 1 && i != 3)
                            {
                                com += rig.points[i].pos * rig.points[i].mass;
                                total_mass += rig.points[i].mass;
                            }

                        com /= total_mass;
                        var offset = target_com - com;
                        for (var i = 0; i < rig.points.Count; ++i)
                            if (i != 1 && i != 3)
                                rig.points[i].pos += offset * 0.2f;

                        // Apply torque to keep torso upright and forward-facing
                        var step_sqrd = step * step;
                        var force = 20f;
                        var forward = math.normalize(math.cross(rig.points[0].pos - rig.points[2].pos,
                            rig.points[0].pos - rig.points[4].pos));
                        var flat_forward = math.normalize(new float3(forward[0], 0, forward[2]));
                        var top_force = (lean * flat_forward + new float3(0, 1, 0)) * force;
                        rig.points[4].pos += step_sqrd * -top_force;
                        rig.points[0].pos += step_sqrd * top_force * 0.5f;
                        rig.points[2].pos += step_sqrd * top_force * 0.5f;
                        rig.points[0].pos[2] -= step_sqrd * effective_vel[0] * 2.0f * (1.0f - skate_amount);
                        rig.points[2].pos[2] += step_sqrd * effective_vel[0] * 2.0f * (1.0f - skate_amount);

                        // Add rotational force to body if needed
                        for (var i = 0; i < 2; ++i)
                        {
                            var walk_rotate = math.cos((walk_time + tilt_offset) * math.PI * 2.0f + math.PI * i) * 0.2f;
                            var gallop_rotate = math.cos(math.PI * i) * (gallop_hip_rotate * (1.0f - quad_amount));
                            var rotate = math.lerp(walk_rotate, gallop_rotate, gallop_amount);
                            rig.points[i * 2].pos[0] += step_sqrd * -3.0f * rotate * effective_vel[0] / speed_mult;
                        }

                        // Move arms out to sides
                        var speed = math.abs(effective_vel[0]) / max_speed;
                        for (var i = 0; i < 2; ++i)
                        {
                            var arms_up =
                                math.abs(speed * (math.sin(Time.time * (i == 1 ? 2.5f : 2.3f)) * 0.3f + 0.7f)) *
                                (1.0f - gallop_amount);
                            rig.points[1 + i * 2].pos += step_sqrd * (rig.points[0].pos - rig.points[2].pos) *
                                                         (1.5f + speed * 2.0f + arms_up * 2.0f) * (1 - i * 2) * 2f;
                            rig.points[1 + i * 2].pos[1] += step_sqrd * 10.0f * arms_up * arms_up;
                            rig.bones[i].length[1] = rig.bones[0].length[0] / 0.4f *
                                                     math.lerp(
                                                         math.lerp(0.95f, 0.8f,
                                                             math.min(speed * 0.25f, 1.0f) +
                                                             math.sin(arms_up * math.PI) * 0.1f), 1.0f, gallop_amount);
                        }

                        PreventHandsFromCrossingBody(rig);

                        // Move hands towards swing handholds if transitioning out of swing state
                        if (on_branch < 1.0f && !b_wants_to_swing)
                        {
                            rig.points[1].pos = math.lerp(rig.points[1].pos, swingMovementSystem.simple_rig.points[1].pos,
                                (1.0f - on_branch) * 0.2f);
                            rig.points[3].pos = math.lerp(rig.points[3].pos, swingMovementSystem.simple_rig.points[3].pos,
                                (1.0f - on_branch) * 0.2f);
                        }

                        // Make sure hands don't go through floor
                        for (var i = 0; i < 2; ++i)
                            rig.points[i * 2 + 1].pos[1] = math.max(rig.points[i * 2 + 1].pos[1],
                                BranchesHeight(rig.points[i * 2 + 1].pos[0]));

                        for (var i = 0; i < 2; ++i) rig.EnforceDistanceConstraints();
                    }

                    rig.EndSim();

                    // Calculate leg targets
                    for (var i = 0; i < 2; ++i)
                    {
                        var offset = math.lerp(gallop_offset, quad_gallop_offset, quad_amount);
                        var time_val = walk_time * math.PI * 2.0f + math.PI * i * offset;
                        walkMovementSystem.limb_targets[2 + i] = simple_pos;
                        walkMovementSystem.limb_targets[2 + i] +=
                            (move_dir * math.cos(walk_time * math.PI * 2.0f + math.PI * i) * 0.2f - 0.03f) *
                            effective_vel[0] / speed_mult * (1.0f - skate_amount) * (1.0f - gallop_amount);
                        walkMovementSystem.limb_targets[2 + i] += (move_dir * math.cos(time_val) * 0.2f - 0.03f) * effective_vel[0] /
                            speed_mult * (1.0f - skate_amount) * gallop_stride * (1.0f - skate_amount) * gallop_amount;
                        walkMovementSystem.limb_targets[2 + i] += (rig.points[0].pos - rig.points[2].pos) * (1.0f - 2.0f * i) *
                                                    (0.3f + 0.3f * skate_amount);
                        walkMovementSystem.limb_targets[2 + i][1] = BranchesHeight(walkMovementSystem.limb_targets[2 + i][0]);
                        walkMovementSystem.limb_targets[2 + i][1] += (-math.sin(walk_time * math.PI * 2.0f + math.PI * i) + 1.0f) *
                                                       0.2f *
                                                       (math.pow(math.abs(effective_vel[0]) + 1.0f, 0.3f) - 1.0f) *
                                                       (1.0f - skate_amount) * (1.0f - gallop_amount);
                        walkMovementSystem.limb_targets[2 + i][1] += (-math.sin(time_val) + 1.0f) * gallop_stride_height *
                                                       (math.pow(math.abs(effective_vel[0]) + 1.0f, 0.3f) - 1.0f) *
                                                       (1.0f - skate_amount) * gallop_amount;
                    }
                }
            }

            {
                // Combine source rigs into display rig
                // Start calculating COM line if needed
                var old_com = float3.zero;
                if (debug_info.draw_com_line)
                {
                    var total_mass = 0.0f;
                    var points = displayMovementSystem.simple_rig.points;
                    for (var i = 0; i < points.Count; ++i)
                        if (points[i].pinned == false)
                        {
                            old_com += points[i].pos * points[i].mass;
                            total_mass += points[i].mass;
                        }

                    old_com /= total_mass;
                }

                // Interpolate between source rigs
                for (var i = 0; i < displayMovementSystem.simple_rig.points.Count; ++i)
                {
                    displayMovementSystem.simple_rig.points[i].old_pos = displayMovementSystem.simple_rig.points[i].pos;
                    displayMovementSystem.simple_rig.points[i].pos = math.lerp(swingMovementSystem.simple_rig.points[i].pos,
                        walkMovementSystem.simple_rig.points[i].pos, on_branch);
                    displayMovementSystem.simple_rig.points[i].pos = math.lerp(displayMovementSystem.simple_rig.points[i].pos,
                        jumpMovementSystem.simple_rig.points[i].pos, in_air_amount);
                }

                for (var i = 0; i < 2; ++i) displayMovementSystem.simple_rig.EnforceDistanceConstraints();
                for (var i = 0; i < 4; ++i)
                {
                    displayMovementSystem.limb_targets[i] = math.lerp(swingMovementSystem.limb_targets[i], walkMovementSystem.limb_targets[i], on_branch);
                    displayMovementSystem.limb_targets[i] = math.lerp(displayMovementSystem.limb_targets[i], jumpMovementSystem.limb_targets[i], in_air_amount);
                }

                body_compress_amount = math.lerp(swingMovementSystem.body_compress_amount, walkMovementSystem.body_compress_amount, on_branch);
                body_compress_amount = math.lerp(body_compress_amount, jumpMovementSystem.body_compress_amount, in_air_amount);

                // Draw COM line
                if (debug_info.draw_com_line)
                {
                    var com = float3.zero;
                    {
                        var total_mass = 0.0f;
                        var points = displayMovementSystem.simple_rig.points;
                        for (var i = 0; i < points.Count; ++i)
                            if (points[i].pinned == false)
                            {
                                com += points[i].pos * points[i].mass;
                                total_mass += points[i].mass;
                            }

                        com /= total_mass;
                    }
                    debug_info.com_lines.Add(DebugDraw.Line(old_com, com, Color.green, DebugDraw.Lifetime.Persistent,
                        DebugDraw.Type.Xray));
                }
            }

            if (debug_info.draw_simple_point)
                DebugDraw.Sphere(simple_pos, Color.yellow, Vector3.one * 0.1f, Quaternion.identity,
                    DebugDraw.Lifetime.OneFixedUpdate, DebugDraw.Type.Xray);

            // Move game camera to track character       
            {
                var cam_pos = Camera.main.transform.position;
                // Get COM
                var total_mass = 0.0f;
                var com = new float3(0.0f, 0.0f, 0.0f);
                var points = displayMovementSystem.simple_rig.points;
                for (var i = 0; i < points.Count; ++i)
                {
                    com += points[i].pos * points[i].mass;
                    total_mass += points[i].mass;
                }

                com /= total_mass;
                // Track COM position
                cam_pos[0] = com[0] + simple_vel[0] * 0.1f;
                Camera.main.transform.position = cam_pos;
            }
        }

        // Skinning data
        private class DisplayBone
        {
            public float3 bind_pos;
            public quaternion bind_rot;
            public Transform transform;

            public void Bind(Transform transform)
            {
                this.transform = transform;
                bind_pos = transform.position;
                bind_rot = transform.rotation;
            }
        }

        private class MovementSystem
        {
            public readonly float3[] limb_targets = new float3[4];
            public readonly Verlet.System simple_rig = new();
            public float body_compress_amount;
            public float3 target_com;
        }

        // Debug draw values and window
        private class DebugInfo
        {
            public readonly List<DebugDraw.DebugDrawLine> com_lines = new();
            public readonly bool draw_com_line = false;
            public readonly bool draw_elbow_ik_target = false;
            public readonly bool draw_hand_pull = false;
            public readonly bool draw_head_look = false;
            private readonly int draw_layer = 2;
            public readonly bool draw_smoothing = false;
            public readonly bool draw_trajectory = false;
            private readonly int force_gait = 1;
            public bool draw_display_complete_rig;
            public bool draw_display_simple_rig;
            public bool draw_gibbon = true;
            public bool draw_ik_final;
            public bool draw_jump_rig;
            public bool draw_simple_point;
            public bool draw_swing_rig;
            public bool draw_walk_rig;
            public bool force_gallop;
            public bool force_quad;
            public bool force_run;
            public bool force_skate;

            private string[] gaits =
            {
                "Normal",
                "Skate",
                "Biped Run",
                "Biped Gallop",
                "Quadruped Gallop"
            };

            private string[] layers =
            {
                "Skinned",
                "IK",
                "Rig",
                "Simple Rig",
                "Source Rigs",
                "Particle"
            };

            public void DrawWindow()
            {
                // if(ImGui.Begin("Debug Visualization")){
                //     if(ImGui.Combo("Draw", ref draw_layer, layers)){
                draw_gibbon = draw_layer == 0;
                draw_ik_final = draw_layer == 1;
                draw_display_complete_rig = draw_layer == 2;
                draw_display_simple_rig = draw_layer == 3;
                draw_walk_rig = draw_layer == 4;
                draw_swing_rig = draw_layer == 4;
                draw_jump_rig = draw_layer == 4;
                draw_simple_point = draw_layer == 5;
                //     }
                //     if(ImGui.Combo("Gait", ref force_gait, gaits)){
                force_skate = force_gait == 1;
                force_run = force_gait == 2;
                force_gallop = force_gait == 3;
                force_quad = force_gait == 4;
                //     }
                //     if(ImGui.Checkbox("Draw COM line", ref draw_com_line)){
                if (!draw_com_line)
                {
                    foreach (var line in com_lines) DebugDraw.Remove(line);

                    com_lines.Clear();
                }
                //         }
                //     }
                //     ImGui.Checkbox("Draw path smoothing", ref draw_smoothing);
                //     ImGui.Checkbox("Draw hand pull", ref draw_hand_pull);
                //     ImGui.Checkbox("Draw jump trajectory", ref draw_trajectory);
                //     ImGui.Checkbox("Draw head look", ref draw_head_look);
                //     ImGui.Checkbox("Draw elbow IK target", ref draw_elbow_ik_target);
                //     
                //     bool slow_motion = (Time.timeScale != 1.0f);
                //     if(ImGui.Checkbox("Slow motion [tab]", ref slow_motion)) {
                //         Time.timeScale = (Time.timeScale == 1.0f)?0.1f:1.0f;
                //     }
                // }
                // ImGui.End();
            }
        }
    }
}