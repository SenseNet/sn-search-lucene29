using SenseNet.Security;
// ReSharper disable CoVariantArrayConversion

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    /// <summary>
    /// A mirror implementation of the PermissionType class found in the repository.
    /// </summary>
    public class PermissionType : PermissionTypeBase
    {
        public static readonly PermissionType See;
        public static readonly PermissionType Preview;
        public static readonly PermissionType PreviewWithoutWatermark;
        public static readonly PermissionType PreviewWithoutRedaction;
        public static readonly PermissionType Open;
        public static readonly PermissionType OpenMinor;
        public static readonly PermissionType RecallOldVersion;

        private PermissionType(string name, int index) : base(name, index) { }

        static PermissionType()
        {
            See = new PermissionType("See", 0);
            Preview = new PermissionType("Preview", 1) { Allows = new[] { See } };
            PreviewWithoutWatermark = new PermissionType("PreviewWithoutWatermark", 2) { Allows = new[] { Preview } };
            PreviewWithoutRedaction = new PermissionType("PreviewWithoutRedaction", 3) { Allows = new[] { Preview } };
            Open = new PermissionType("Open", 4) { Allows = new[] { PreviewWithoutWatermark, PreviewWithoutRedaction } };
            OpenMinor = new PermissionType("OpenMinor", 5) { Allows = new[] { Open } };
            RecallOldVersion = new PermissionType("RecallOldVersion", 12) { Allows = new[] { OpenMinor } };
        }
    }
}
