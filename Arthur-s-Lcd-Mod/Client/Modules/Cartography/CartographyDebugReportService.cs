#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using LcdMod.Common.Helpers;
using Adk.Compression.Zip;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRageMath;

namespace LcdMod.Client.Modules.Cartography
{
    internal sealed class CartographyDebugReportService
    {
        const int DEFAULT_FACE_SIDE = 256;
        const int MINIMUM_FACE_SIDE = 16;
        const int MAXIMUM_FACE_SIDE = 1024;

        static readonly CartographyLayer[] Layers =
        {
            CartographyLayer.Satellite,
            CartographyLayer.Terrain,
            CartographyLayer.Biomes
        };

        sealed class PlanetTest
        {
            public string DefinitionId;
            public string GeneratorSubtype;
            public string FolderName;
            public bool IsBaseGame;
            public bool HasModItem;
            public MyObjectBuilder_Checkpoint.ModItem ModItem;
            public string ArchiveFileName;
            public readonly List<LayerTestResult> Results = new List<LayerTestResult>();
        }

        sealed class LayerTestResult
        {
            public CartographyLayer Layer;
            public bool Success;
            public bool Cancelled;
            public string Error;
            public long GenerationMilliseconds;
            public long BitmapMilliseconds;
            public int FaceWidth;
            public int FaceHeight;
            public int MipCount;
            public string BitmapFileName;
            public byte[] BitmapBytes;
            public long MagentaPixels;
            public long TransparentPixels;
            public long TotalPixels;
            public string DiagnosticReport;
        }

        sealed class BitmapBuildWork
        {
            public byte[] Bytes;
            public long MagentaPixels;
            public long TransparentPixels;
            public long TotalPixels;
            public long ElapsedMilliseconds;
            public Exception Error;
        }

        readonly CartographyModule _cartography;
        readonly List<PlanetTest> _planets = new List<PlanetTest>();
        CartographyTicket _activeTicket;
        PlanetTest _currentPlanet;
        int _currentPlanetIndex;
        int _currentLayerIndex;
        int _requestedFaceSide;
        int _runGeneration;
        int _archivesWritten;
        int _archiveWriteFailures;
        string _runTimestamp;
        bool _isRunning;

        public CartographyDebugReportService(CartographyModule cartography)
        {
            if (cartography == null)
                throw new ArgumentNullException(nameof(cartography));

            _cartography = cartography;
        }

        internal void RunCommand(string[] args)
        {
            if (_isRunning)
            {
                Show("Cartography definition test is already running.", "Yellow");
                return;
            }

            if (MyDefinitionManager.Static == null)
            {
                Show("Planet definitions are not ready.", "Red");
                return;
            }

            int faceSide;
            if (!TryReadFaceSide(args, out faceSide))
            {
                Show(
                    "Usage: /lcdmod testcartography [face-side " +
                    MINIMUM_FACE_SIDE.ToString(CultureInfo.InvariantCulture) + "-" +
                    MAXIMUM_FACE_SIDE.ToString(CultureInfo.InvariantCulture) + "]",
                    "Red");
                return;
            }

            List<PlanetTest> planets;
            try
            {
                planets = CollectPlanetDefinitions();
            }
            catch (Exception error)
            {
                ErrorHandlerHelper.LogError(error, typeof(CartographyDebugReportService));
                Show("Could not enumerate planet definitions: " + error.Message, "Red");
                return;
            }

            if (planets.Count == 0)
            {
                Show("No loaded planet generator definitions were found.", "Yellow");
                return;
            }

            _runGeneration++;
            _isRunning = true;
            _requestedFaceSide = faceSide;
            _runTimestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            _currentPlanetIndex = -1;
            _currentLayerIndex = 0;
            _archivesWritten = 0;
            _archiveWriteFailures = 0;
            _activeTicket = null;
            _currentPlanet = null;
            _planets.Clear();
            _planets.AddRange(planets);
            AssignArchiveFileNames(_planets, _runTimestamp);

            Show(
                "Cartography test started for " + _planets.Count.ToString(CultureInfo.InvariantCulture) +
                " planet definitions at " + faceSide.ToString(CultureInfo.InvariantCulture) + " px.",
                "Yellow");
            StartNextPlanet(_runGeneration);
        }

        public void Unload()
        {
            _runGeneration++;
            _isRunning = false;
            if (_activeTicket != null)
                _activeTicket.Cancel();

            _activeTicket = null;
            _currentPlanet = null;
            _planets.Clear();
        }

        void StartNextPlanet(int runGeneration)
        {
            if (!IsCurrentRun(runGeneration))
                return;

            _currentPlanetIndex++;
            if (_currentPlanetIndex >= _planets.Count)
            {
                CompleteRun(runGeneration);
                return;
            }

            _currentPlanet = _planets[_currentPlanetIndex];
            _currentLayerIndex = 0;
            _currentPlanet.Results.Clear();
            StartCurrentLayer(runGeneration);
        }

        void StartCurrentLayer(int runGeneration)
        {
            if (!IsCurrentRun(runGeneration) || _currentPlanet == null)
                return;

            if (_currentLayerIndex >= Layers.Length)
            {
                CompleteCurrentPlanet(runGeneration);
                return;
            }

            CartographyLayer layer = Layers[_currentLayerIndex];
            PlanetTest planet = _currentPlanet;
            ShowProgress(planet, layer);

            var request = new CartographyRequest
            {
                PlanetGeneratorSubtype = planet.GeneratorSubtype,
                Projection = CartographyProjection.CubemapFaces,
                Layer = layer,
                MaximumFaceSide = _requestedFaceSide,
                ReturnColorCubemap = true,
                IncludeDiagnostics = layer == CartographyLayer.Satellite
            };

            var stopwatch = Stopwatch.StartNew();
            CartographyTicket ticket = null;
            bool completedSynchronously = false;
            try
            {
                ticket = _cartography.RequestMap(
                    request,
                    delegate(CartographyResult result)
                    {
                        completedSynchronously = ticket == null;
                        stopwatch.Stop();
                        if (ticket != null && ReferenceEquals(_activeTicket, ticket))
                            _activeTicket = null;
                        HandleMapResult(
                            runGeneration,
                            planet,
                            layer,
                            stopwatch.ElapsedMilliseconds,
                            result);
                    });

                if (!completedSynchronously && IsCurrentPlanet(runGeneration, planet))
                    _activeTicket = ticket;
            }
            catch (Exception error)
            {
                stopwatch.Stop();
                planet.Results.Add(new LayerTestResult
                {
                    Layer = layer,
                    Success = false,
                    Error = error.ToString(),
                    GenerationMilliseconds = stopwatch.ElapsedMilliseconds
                });
                AdvanceLayer(runGeneration, planet);
            }
        }

        void HandleMapResult(
            int runGeneration,
            PlanetTest planet,
            CartographyLayer layer,
            long generationMilliseconds,
            CartographyResult result)
        {
            if (!IsCurrentPlanet(runGeneration, planet))
                return;

            var layerResult = new LayerTestResult
            {
                Layer = layer,
                GenerationMilliseconds = generationMilliseconds,
                Success = result != null && result.Success,
                Cancelled = result != null && result.Cancelled,
                Error = result == null ? "Cartography returned no result." : result.Error,
                FaceWidth = result == null ? 0 : result.FaceWidth,
                FaceHeight = result == null ? 0 : result.FaceHeight,
                MipCount = result != null && result.ColorCubemap != null
                    ? result.ColorCubemap.MipCount
                    : 0,
                DiagnosticReport = result == null ? null : result.DiagnosticReport
            };

            if (!layerResult.Success || result.ColorCubemap == null)
            {
                if (layerResult.Success)
                {
                    layerResult.Success = false;
                    layerResult.Error = "Cartography succeeded without returning a color cubemap.";
                }

                planet.Results.Add(layerResult);
                AdvanceLayer(runGeneration, planet);
                return;
            }

            layerResult.BitmapFileName = layer.ToString().ToLowerInvariant() + ".bmp";
            var bitmapWork = new BitmapBuildWork();
            PlanetColorCubemap cubemap = result.ColorCubemap;

            MyAPIGateway.Parallel.Start(
                delegate
                {
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        BuildBitmap(cubemap, bitmapWork);
                    }
                    catch (Exception error)
                    {
                        bitmapWork.Error = error;
                    }
                    finally
                    {
                        stopwatch.Stop();
                        bitmapWork.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    }
                },
                delegate
                {
                    if (!IsCurrentPlanet(runGeneration, planet))
                        return;

                    layerResult.BitmapMilliseconds = bitmapWork.ElapsedMilliseconds;
                    if (bitmapWork.Error != null)
                    {
                        layerResult.Success = false;
                        layerResult.Error = "Bitmap export failed: " + bitmapWork.Error;
                    }
                    else
                    {
                        layerResult.BitmapBytes = bitmapWork.Bytes;
                        layerResult.MagentaPixels = bitmapWork.MagentaPixels;
                        layerResult.TransparentPixels = bitmapWork.TransparentPixels;
                        layerResult.TotalPixels = bitmapWork.TotalPixels;
                    }

                    planet.Results.Add(layerResult);
                    AdvanceLayer(runGeneration, planet);
                });
        }

        void AdvanceLayer(int runGeneration, PlanetTest planet)
        {
            if (!IsCurrentPlanet(runGeneration, planet))
                return;

            _currentLayerIndex++;
            LcdModClientComponent.RunNextFrame.Add(
                delegate { StartCurrentLayer(runGeneration); });
        }

        void CompleteCurrentPlanet(int runGeneration)
        {
            if (!IsCurrentRun(runGeneration) || _currentPlanet == null)
                return;

            PlanetTest planet = _currentPlanet;
            try
            {
                WritePlanetArchive(planet);
                _archivesWritten++;
            }
            catch (Exception error)
            {
                _archiveWriteFailures++;
                ErrorHandlerHelper.LogError(error, typeof(CartographyDebugReportService));
                Show(
                    "Could not write cartography archive for " + planet.GeneratorSubtype +
                    ": " + error.Message,
                    "Red");
            }
            finally
            {
                planet.Results.Clear();
            }

            StartNextPlanet(runGeneration);
        }

        void CompleteRun(int runGeneration)
        {
            if (!IsCurrentRun(runGeneration))
                return;

            _isRunning = false;
            _activeTicket = null;
            _currentPlanet = null;
            _planets.Clear();

            string path = Path.Combine(
                MyAPIGateway.Utilities.GamePaths.UserDataPath,
                "Storage",
                MyAPIGateway.Utilities.GamePaths.ModScopeName);
            string summary = "Cartography test complete: " +
                             _archivesWritten.ToString(CultureInfo.InvariantCulture) +
                             " archives saved";
            if (_archiveWriteFailures > 0)
            {
                summary += ", " + _archiveWriteFailures.ToString(CultureInfo.InvariantCulture) +
                           " archive writes failed";
            }

            Show(summary + ". " + path, _archiveWriteFailures == 0 ? "White" : "Yellow");
        }

        void WritePlanetArchive(PlanetTest planet)
        {
            var entries = new List<MinimalZip.Entry>();
            for (int i = 0; i < planet.Results.Count; i++)
            {
                LayerTestResult result = planet.Results[i];
                if (result.Success && result.BitmapBytes != null)
                {
                    entries.Add(new MinimalZip.Entry(
                        result.BitmapFileName,
                        result.BitmapBytes));
                }
            }

            entries.Insert(
                0,
                new MinimalZip.Entry(
                    "report.txt",
                    Encoding.UTF8.GetBytes(BuildReport(planet))));

            using (var writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(
                       planet.ArchiveFileName,
                       typeof(LcdModClientComponent)))
            {
                MinimalZip.Write(writer.BaseStream, entries);
                writer.Flush();
            }
        }

        string BuildReport(PlanetTest planet)
        {
            var builder = new StringBuilder();
            builder.AppendLine("LCD Mod cartography cubemap diagnostic");
            builder.AppendLine("Generated UTC: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendLine("Definition: " + NullDisplay(planet.DefinitionId));
            builder.AppendLine("Generator subtype: " + NullDisplay(planet.GeneratorSubtype));
            builder.AppendLine("Planet data folder: " + NullDisplay(planet.FolderName));
            builder.AppendLine("Source: " + (planet.IsBaseGame ? "base game" : "mod"));
            if (!planet.IsBaseGame)
            {
                builder.AppendLine("Mod item available: " + planet.HasModItem);
                builder.AppendLine("Mod name: " + NullDisplay(planet.ModItem.Name));
                builder.AppendLine("Published service: " + NullDisplay(planet.ModItem.PublishedServiceName));
                builder.AppendLine("Published file id: " + planet.ModItem.PublishedFileId);
            }

            builder.AppendLine("Requested maximum face side: " +
                               _requestedFaceSide.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Bitmap format: 32-bit BGRA BMP, top-down rows");
            builder.AppendLine("Bitmap face layout: row 1 = back | down | front; row 2 = left | right | up");
            builder.AppendLine();

            int successes = 0;
            for (int i = 0; i < planet.Results.Count; i++)
            {
                LayerTestResult result = planet.Results[i];
                builder.AppendLine("[" + result.Layer + "]");
                builder.AppendLine("Success: " + result.Success);
                builder.AppendLine("Cancelled: " + result.Cancelled);
                builder.AppendLine("Generation time ms: " +
                                   result.GenerationMilliseconds.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("Bitmap export time ms: " +
                                   result.BitmapMilliseconds.ToString(CultureInfo.InvariantCulture));

                if (result.Success)
                {
                    successes++;
                    builder.AppendLine("Face size: " +
                                       result.FaceWidth.ToString(CultureInfo.InvariantCulture) + "x" +
                                       result.FaceHeight.ToString(CultureInfo.InvariantCulture));
                    builder.AppendLine("Mip count: " + result.MipCount.ToString(CultureInfo.InvariantCulture));
                    builder.AppendLine("Bitmap: " + result.BitmapFileName);
                    builder.AppendLine("Cubemap texels: " +
                                       result.TotalPixels.ToString(CultureInfo.InvariantCulture));
                    builder.AppendLine("Exact magenta fallback candidates: " +
                                       result.MagentaPixels.ToString(CultureInfo.InvariantCulture));
                    builder.AppendLine("Transparent texels: " +
                                       result.TransparentPixels.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.AppendLine("Error: " + NullDisplay(result.Error));
                }

                if (!string.IsNullOrWhiteSpace(result.DiagnosticReport))
                {
                    builder.AppendLine();
                    builder.AppendLine("Color resolver diagnostics:");
                    builder.AppendLine(result.DiagnosticReport.TrimEnd());
                }

                builder.AppendLine();
            }

            builder.AppendLine("Summary: " + successes.ToString(CultureInfo.InvariantCulture) + "/" +
                               Layers.Length.ToString(CultureInfo.InvariantCulture) +
                               " layers generated successfully.");
            builder.AppendLine(
                "Note: exact magenta (255,0,255) is the cartography missing-material fallback and can indicate unresolved user-content texture dependencies.");
            return builder.ToString();
        }

        static void BuildBitmap(PlanetColorCubemap cubemap, BitmapBuildWork work)
        {
            if (cubemap == null)
                throw new ArgumentNullException(nameof(cubemap));
            if (work == null)
                throw new ArgumentNullException(nameof(work));

            int faceSide = cubemap.BaseResolution;
            int width = checked(faceSide * 3);
            int height = checked(faceSide * 2);
            int pixelBytes = checked(width * height * 4);
            byte[] bitmap = new byte[checked(54 + pixelBytes)];

            bitmap[0] = (byte)'B';
            bitmap[1] = (byte)'M';
            WriteInt32(bitmap, 2, bitmap.Length);
            WriteInt32(bitmap, 10, 54);
            WriteInt32(bitmap, 14, 40);
            WriteInt32(bitmap, 18, width);
            WriteInt32(bitmap, 22, -height);
            WriteInt16(bitmap, 26, 1);
            WriteInt16(bitmap, 28, 32);
            WriteInt32(bitmap, 34, pixelBytes);

            long magenta = 0;
            long transparent = 0;
            int offset = 54;
            for (int tileY = 0; tileY < 2; tileY++)
            {
                for (int y = 0; y < faceSide; y++)
                {
                    float v = (y + 0.5f) / faceSide;
                    for (int tileX = 0; tileX < 3; tileX++)
                    {
                        PlanetCubeFace face = PlanetMapSource.ExportOrder[tileY * 3 + tileX];
                        for (int x = 0; x < faceSide; x++)
                        {
                            float u = (x + 0.5f) / faceSide;
                            Color color = cubemap.SampleFace(face, u, v, 0);
                            bitmap[offset++] = color.B;
                            bitmap[offset++] = color.G;
                            bitmap[offset++] = color.R;
                            bitmap[offset++] = color.A;

                            if (color.R == 255 && color.G == 0 && color.B == 255)
                                magenta++;
                            if (color.A == 0)
                                transparent++;
                        }
                    }
                }
            }

            work.Bytes = bitmap;
            work.MagentaPixels = magenta;
            work.TransparentPixels = transparent;
            work.TotalPixels = checked((long)faceSide * faceSide * 6L);
        }

        static List<PlanetTest> CollectPlanetDefinitions()
        {
            var result = new List<PlanetTest>();
            foreach (MyPlanetGeneratorDefinition definition in
                     MyDefinitionManager.Static.GetPlanetsGeneratorsDefinitions())
            {
                if (definition == null)
                    continue;

                bool isBaseGame = definition.Context == null || definition.Context.IsBaseGame;
                result.Add(new PlanetTest
                {
                    DefinitionId = definition.Id.ToString(),
                    GeneratorSubtype = definition.Id.SubtypeName,
                    FolderName = definition.FolderName,
                    IsBaseGame = isBaseGame,
                    HasModItem = definition.Context != null && !definition.Context.IsBaseGame,
                    ModItem = definition.Context != null
                        ? definition.Context.ModItem
                        : default(MyObjectBuilder_Checkpoint.ModItem)
                });
            }

            result.Sort(delegate(PlanetTest left, PlanetTest right)
            {
                int subtype = string.Compare(
                    left.GeneratorSubtype,
                    right.GeneratorSubtype,
                    StringComparison.OrdinalIgnoreCase);
                return subtype != 0
                    ? subtype
                    : string.Compare(left.DefinitionId, right.DefinitionId, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        static void AssignArchiveFileNames(List<PlanetTest> planets, string timestamp)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < planets.Count; i++)
            {
                PlanetTest planet = planets[i];
                string baseName = "cartography_" + SanitizeFileName(planet.GeneratorSubtype) + "_" + timestamp;
                string candidate = baseName + ".zip";
                int duplicate = 2;
                while (!used.Add(candidate))
                {
                    candidate = baseName + "_" +
                                duplicate.ToString(CultureInfo.InvariantCulture) + ".zip";
                    duplicate++;
                }

                planet.ArchiveFileName = candidate;
            }
        }

        static bool TryReadFaceSide(string[] args, out int faceSide)
        {
            faceSide = DEFAULT_FACE_SIDE;
            if (args == null || args.Length == 0)
                return true;
            if (args.Length != 1 ||
                !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out faceSide))
            {
                return false;
            }

            return faceSide >= MINIMUM_FACE_SIDE && faceSide <= MAXIMUM_FACE_SIDE;
        }

        static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unnamed";

            var builder = new StringBuilder(Math.Min(value.Length, 80));
            for (int i = 0; i < value.Length && builder.Length < 80; i++)
            {
                char character = value[i];
                if (char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.')
                    builder.Append(character);
                else
                    builder.Append('_');
            }

            return builder.Length == 0 ? "unnamed" : builder.ToString();
        }

        static string NullDisplay(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
        }

        bool IsCurrentRun(int runGeneration)
        {
            return _isRunning && runGeneration == _runGeneration;
        }

        bool IsCurrentPlanet(int runGeneration, PlanetTest planet)
        {
            return IsCurrentRun(runGeneration) && ReferenceEquals(_currentPlanet, planet);
        }

        void ShowProgress(PlanetTest planet, CartographyLayer layer)
        {
            Show(
                "Cartography " + (_currentPlanetIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" +
                _planets.Count.ToString(CultureInfo.InvariantCulture) + ": " +
                NullDisplay(planet.GeneratorSubtype) + " - " + layer,
                "Yellow");
        }

        static void Show(string text, string font = "White")
        {
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.ShowNotification(text, 5000, font);
        }

        static void WriteInt16(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
        }

        static void WriteInt32(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }
    }
}
#endif
