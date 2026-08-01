using System;

namespace LcdMod.Common.Imaging
{
    /// <summary>
    /// Converts texture references expanded to an operating-system path back to the
    /// content-relative form expected by the Space Engineers mod file APIs.
    /// </summary>
    public static class ContentTexturePath
    {
        const string TextureRoot = "Textures";

        public static bool TryNormalize(
            string path,
            out string relativePath,
            out string failureReason)
        {
            relativePath = null;
            failureReason = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                failureReason = "Texture path is empty.";
                return false;
            }

            string normalized = CollapseSeparators(path.Trim().Replace('\\', '/'));
            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);

            int textureSegmentStart = FindTextureSegment(normalized);
            if (textureSegmentStart < 0)
            {
                failureReason = "Texture path has no Textures directory segment.";
                return false;
            }

            int textureSegmentEnd = normalized.IndexOf('/', textureSegmentStart);
            if (textureSegmentEnd < 0 || textureSegmentEnd + 1 >= normalized.Length)
            {
                failureReason = "Texture path does not name a file below Textures/.";
                return false;
            }

            string remainder = normalized.Substring(textureSegmentEnd + 1);
            if (ContainsTraversalSegment(remainder))
            {
                failureReason = "Texture path contains a parent-directory traversal segment.";
                return false;
            }

            relativePath = TextureRoot + "/" + remainder;
            return true;
        }

        static int FindTextureSegment(string path)
        {
            int segmentStart = 0;
            for (int i = 0; i <= path.Length; i++)
            {
                if (i < path.Length && path[i] != '/')
                    continue;

                int segmentLength = i - segmentStart;
                if (segmentLength == TextureRoot.Length &&
                    string.Compare(
                        path,
                        segmentStart,
                        TextureRoot,
                        0,
                        TextureRoot.Length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                    return segmentStart;

                segmentStart = i + 1;
            }

            return -1;
        }

        static bool ContainsTraversalSegment(string path)
        {
            int segmentStart = 0;
            for (int i = 0; i <= path.Length; i++)
            {
                if (i < path.Length && path[i] != '/')
                    continue;

                int segmentLength = i - segmentStart;
                if (segmentLength == 2 &&
                    path[segmentStart] == '.' &&
                    path[segmentStart + 1] == '.')
                    return true;

                segmentStart = i + 1;
            }

            return false;
        }

        static string CollapseSeparators(string path)
        {
            while (path.IndexOf("//", StringComparison.Ordinal) >= 0)
                path = path.Replace("//", "/");

            return path;
        }
    }
}
