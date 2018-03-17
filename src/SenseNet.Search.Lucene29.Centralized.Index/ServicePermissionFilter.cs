using System;
using System.Collections.Generic;
using SenseNet.Diagnostics;
using SenseNet.Search.Querying;
using SenseNet.Security;

namespace SenseNet.Search.Lucene29.Centralized.Index
{
    public enum QueryFieldLevel
    {
        /// <summary>
        /// Default value. Means: cannot be used, It is necessary to specify exactly for using it in permission filtering.
        /// </summary>
        NotDefined = 0,
        /// <summary>
        /// Minimum See permission is required.
        /// </summary>
        HeadOnly = 1,
        /// <summary>
        /// Minimum Preview permission is required.
        /// </summary>
        NoBinaryOrFullText = 2,
        /// <summary>
        /// Minimum Open permission is required.
        /// </summary>
        BinaryOrFullText = 3
    }

    internal class ServicePermissionFilter : IPermissionFilter
    {
        private enum DocumentOpenLevel { Denied, See, Preview, Open, OpenMinor }

        private readonly QueryFieldLevel _queryFieldLevel;
        private readonly bool _allVersions;
        private readonly SecurityHandler _security;

        public ServicePermissionFilter(SecurityHandler security, QueryFieldLevel queryFieldLevel, bool allVersions)
        {
            _security = security;
            _allVersions = allVersions;
            _queryFieldLevel = queryFieldLevel;
        }

        public bool IsPermitted(int nodeId, bool isLastPublic, bool isLastDraft)
        {
            var docLevel = GetDocumentLevel(nodeId);

            // pre-check: do not do any other operation
            if (docLevel == DocumentOpenLevel.Denied)
                return false;

            if (_allVersions)
            {
                var canAccesOldVersions = _security.HasPermission(nodeId, PermissionType.RecallOldVersion);
                switch (docLevel)
                {
                    case DocumentOpenLevel.See:
                        return isLastPublic && canAccesOldVersions && _queryFieldLevel <= QueryFieldLevel.HeadOnly;
                    case DocumentOpenLevel.Preview:
                        return isLastPublic && canAccesOldVersions && _queryFieldLevel <= QueryFieldLevel.NoBinaryOrFullText;
                    case DocumentOpenLevel.Open:
                        return isLastPublic;
                    case DocumentOpenLevel.OpenMinor:
                        return canAccesOldVersions;
                    case DocumentOpenLevel.Denied:
                        return false;
                    default:
                        throw new NotSupportedException("##Unknown DocumentOpenLevel");
                }
            }

            switch (docLevel)
            {
                case DocumentOpenLevel.See:
                    return isLastPublic && _queryFieldLevel <= QueryFieldLevel.HeadOnly;
                case DocumentOpenLevel.Preview:
                    return isLastPublic && _queryFieldLevel <= QueryFieldLevel.NoBinaryOrFullText;
                case DocumentOpenLevel.Open:
                    return isLastPublic;
                case DocumentOpenLevel.OpenMinor:
                    return isLastDraft;
                case DocumentOpenLevel.Denied:
                    return false;
                default:
                    throw new NotSupportedException("Unknown DocumentOpenLevel: " + docLevel);
            }
        }

        private DocumentOpenLevel GetDocumentLevel(int nodeId)
        {
            var userId = _security.Context.CurrentUser.Id;
            if (userId == -1)
                return DocumentOpenLevel.OpenMinor;
            if (userId < -1)
                return DocumentOpenLevel.Denied;

            List<int> identities;
            try
            {
                identities = _security.GetIdentitiesByMembership(nodeId);
            }
            catch (EntityNotFoundException)
            {
                return DocumentOpenLevel.Denied;
            }

            List<AceInfo> entries;
            try
            {
                entries = _security.GetEffectiveEntries(nodeId);
            }
            catch (Exception ex) // LOGGED
            {
                //TODO: collect aggregated errors per query instead of logging every error
                SnLog.WriteWarning($"GetEffectiveEntries threw an exception for id {nodeId}. Error: {ex}");
                return DocumentOpenLevel.Denied;
            }

            var allowBits = 0UL;
            var denyBits = 0UL;
            foreach (var entry in entries)
            {
                if (identities.Contains(entry.IdentityId))
                {
                    allowBits |= entry.AllowBits;
                    denyBits |= entry.DenyBits;
                }
            }
            allowBits = allowBits & ~denyBits;
            var docLevel = DocumentOpenLevel.Denied;
            if ((allowBits & PermissionType.See.Mask) > 0)
                docLevel = DocumentOpenLevel.See;
            if ((allowBits & PermissionType.Preview.Mask) > 0)
                docLevel = DocumentOpenLevel.Preview;
            if ((allowBits & PermissionType.PreviewWithoutRedaction.Mask) > 0)
                docLevel = DocumentOpenLevel.Open;
            if ((allowBits & PermissionType.OpenMinor.Mask) > 0)
                docLevel = DocumentOpenLevel.OpenMinor;
            return docLevel;
        }
    }
}
