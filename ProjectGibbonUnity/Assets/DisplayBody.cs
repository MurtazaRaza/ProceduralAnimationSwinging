namespace Wolfire
{
    public partial class GibbonControl
    {
        private class DisplayBody
        {
            public readonly DisplayBone arm_bottom_l = new();
            public readonly DisplayBone arm_bottom_r = new();
            public readonly DisplayBone arm_top_l = new();
            public readonly DisplayBone arm_top_r = new();
            public readonly DisplayBone belly = new();
            public readonly DisplayBone chest = new();
            public readonly DisplayBone head = new();
            public readonly DisplayBone leg_bottom_l = new();
            public readonly DisplayBone leg_bottom_r = new();
            public readonly DisplayBone leg_top_l = new();
            public readonly DisplayBone leg_top_r = new();
            public readonly DisplayBone pelvis = new();
        }
    }
}