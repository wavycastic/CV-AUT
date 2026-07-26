using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal enum DropSide { TopLeft, BottomLeft, BottomRight, TopRight }

    internal sealed record ExternalAreaModel(Point Left, Point Right, Point Top, Point Bottom)
    {
        public Point[] Diamond => new[] { Top, Right, Bottom, Left };
        public override string ToString() => $"left=({Left.X},{Left.Y}) right=({Right.X},{Right.Y}) top=({Top.X},{Top.Y}) bottom=({Bottom.X},{Bottom.Y})";
    }

    internal sealed class BuilderBaseDropPlanner
    {
        /// <summary>Which extreme of the red-line cloud a percentile lookup is asking for.</summary>
        private enum AreaVertex { LeftMost, RightMost, TopMost, BottomMost }

        private readonly Dictionary<DropSide, List<Point>> _dropLines;
        private readonly Dictionary<DropSide, List<Point>> _furtherDropLines;
        private readonly int _screenWidth;
        private readonly int _screenHeight;

        private BuilderBaseDropPlanner(ExternalAreaModel externalArea, Dictionary<DropSide, List<Point>> rawSides, Dictionary<DropSide, List<Point>> cleanSides, Dictionary<DropSide, List<Point>> dropLines, Dictionary<DropSide, List<Point>> furtherDropLines, IReadOnlyList<Point> rawRedPoints, int screenWidth, int screenHeight, string source, string status)
        {
            ExternalArea = externalArea;
            RawSides = rawSides;
            CleanSides = cleanSides;
            _dropLines = dropLines;
            _furtherDropLines = furtherDropLines;
            RawRedPoints = rawRedPoints;
            _screenWidth = screenWidth;
            _screenHeight = screenHeight;
            Source = source;
            Status = status;
        }

        public ExternalAreaModel ExternalArea { get; }
        public IReadOnlyDictionary<DropSide, List<Point>> RawSides { get; }
        public IReadOnlyDictionary<DropSide, List<Point>> CleanSides { get; }
        public IReadOnlyList<Point> RawRedPoints { get; }
        public IReadOnlyList<Point> LastChosenDropPoints { get; private set; } = Array.Empty<Point>();
        public IEnumerable<Point> AllCleanPoints => CleanSides.Values.SelectMany(p => p);
        public string Source { get; }
        public string Status { get; }
        public int RawRedCount => RawRedPoints.Count;
        public int CleanRedCount => CleanSides.Values.Sum(v => v.Count);
        public int SideCount(DropSide side) => CleanSides.TryGetValue(side, out List<Point>? points) ? points.Count : 0;

        public static BuilderBaseDropPlanner Build(Mat? screenshot, int screenWidth, int screenHeight)
        {
            ExternalAreaModel fallbackExternal = BuildExternalArea(screenWidth, screenHeight);
            List<Point> raw = DetectRedLinePoints(screenshot, screenWidth, screenHeight);
            ExternalAreaModel external = BuildDynamicExternalArea(raw, fallbackExternal, screenWidth, screenHeight);
            Dictionary<DropSide, List<Point>> rawSides = SplitSides(raw, external);
            Dictionary<DropSide, List<Point>> cleanSides = rawSides.ToDictionary(kvp => kvp.Key, kvp => CleanAndSort(kvp.Value, kvp.Key));
            bool enough = cleanSides.Values.All(v => v.Count >= 18) && cleanSides.Values.Sum(v => v.Count) >= 120;

            Dictionary<DropSide, List<Point>> dropLines = new();
            Dictionary<DropSide, List<Point>> furtherDropLines = new();
            foreach (DropSide side in Enum.GetValues<DropSide>())
            {
                List<Point> externalVector = GetMbrVectorOutZone(external, side, screenHeight);
                List<Point> redVector = enough && cleanSides[side].Count >= 18 ? MakeDropLine(cleanSides[side], side) : new List<Point>();
                List<Point> baseLine = redVector.Count > 0 ? redVector : externalVector.Count > 0 ? externalVector : FallbackOuterGreenLine(side);
                int nearOffset = redVector.Count > 0 ? 12 : 0;
                int farOffset = redVector.Count > 0 ? 34 : 22;
                dropLines[side] = MakeDropLine(baseLine, side)
                    .Select(p => OffsetOutside(p, side, nearOffset, screenHeight))
                    .Select(p => TroopDeploymentExecutor.AvoidPotionArea(p, screenHeight))
                    .Where(p => IsSafeDropPoint(p, screenWidth, screenHeight))
                    .ToList();
                furtherDropLines[side] = MakeDropLine(baseLine, side)
                    .Select(p => OffsetOutside(p, side, farOffset, screenHeight))
                    .Select(p => TroopDeploymentExecutor.AvoidPotionArea(p, screenHeight))
                    .Where(p => IsSafeDropPoint(p, screenWidth, screenHeight))
                    .ToList();
            }

            string source = enough ? "dynamic_redline_polygon" : raw.Count >= 24 ? "dynamic_external_polygon" : "mbr_vector_out_zone";
            string status = enough ? "ok_redline_available" : raw.Count >= 24 ? "ok_dynamic_external_fallback" : "ok_mbr_vector_only";
            return new BuilderBaseDropPlanner(external, rawSides, cleanSides, dropLines, furtherDropLines, raw, screenWidth, screenHeight, source, status);
        }

        public List<Point> ChooseDropPoints(string troopName, string sideDirection, Random random)
        {
            bool isRight = string.Equals(sideDirection, "right", StringComparison.OrdinalIgnoreCase);
            DropSide primarySide = isRight ? DropSide.TopRight : DropSide.TopLeft;
            DropSide secondarySide = isRight ? DropSide.BottomRight : DropSide.BottomLeft;

            IReadOnlyList<Point> vectorPrimary = IsFurtherTroop(troopName)
                && _furtherDropLines[primarySide].Count > 0 ? _furtherDropLines[primarySide] : _dropLines[primarySide];
            IReadOnlyList<Point> vectorSecondary = IsFurtherTroop(troopName)
                && _furtherDropLines[secondarySide].Count > 0 ? _furtherDropLines[secondarySide] : _dropLines[secondarySide];

            List<Point> madePrimary = MakeDropPoints(vectorPrimary, primarySide,
                pointsQty: 4, addTiles: 0, random, _screenHeight).Where(p => IsSafeDropPoint(p, _screenWidth, _screenHeight)).ToList();
            List<Point> madeSecondary = MakeDropPoints(vectorSecondary, secondarySide,
                pointsQty: 4, addTiles: 0, random, _screenHeight).Where(p => IsSafeDropPoint(p, _screenWidth, _screenHeight)).ToList();

            List<Point> combined = new List<Point>();
            combined.AddRange(madePrimary);
            combined.AddRange(madeSecondary);

            if (combined.Count == 0)
            {
                combined.AddRange(vectorPrimary.Where(p => IsSafeDropPoint(p, _screenWidth, _screenHeight)));
                combined.AddRange(vectorSecondary.Where(p => IsSafeDropPoint(p, _screenWidth, _screenHeight)));
            }

            LastChosenDropPoints = combined;
            Console.WriteLine($"[BB-ATTACK] phase=deploy_vector status=selected_flank side={sideDirection} primary={primarySide} secondary={secondarySide} troop={troopName} total_points={combined.Count} further={IsFurtherTroop(troopName)}");
            return combined;
        }

        private static ExternalAreaModel BuildExternalArea(int width, int height)
        {
            double sx = width / 860.0;
            double sy = height / 732.0;
            return new ExternalAreaModel(
                new Point((int)Math.Round(66 * sx), (int)Math.Round(299 * sy)),
                new Point((int)Math.Round(794 * sx), (int)Math.Round(299 * sy)),
                new Point((int)Math.Round(430 * sx), (int)Math.Round(55 * sy)),
                new Point((int)Math.Round(430 * sx), (int)Math.Round(555 * sy)));
        }

        private static List<Point> DetectRedLinePoints(Mat? screenshot, int width, int height)
        {
            var result = new List<Point>();
            if (screenshot == null || screenshot.Empty()) return result;
            int xStep = Math.Max(1, screenshot.Width / 860);
            int yStep = Math.Max(3, screenshot.Height / 160);
            int yMin = Math.Max(0, MbrScreenScaling.ScaleY(screenshot, 45));
            int yMax = Math.Min(screenshot.Height - 1, MbrScreenScaling.ScaleY(screenshot, 620));
            for (int y = yMin; y <= yMax; y += yStep)
            {
                for (int x = 20; x < screenshot.Width - 20; x += xStep)
                {
                    Vec3b bgr = screenshot.At<Vec3b>(y, x);
                    int b = bgr.Item0, g = bgr.Item1, r = bgr.Item2;
                    if (r >= 135 && r - g >= 45 && r - b >= 45 && g <= 145 && b <= 145) result.Add(new Point(x, y));
                }
            }
            return result;
        }

        private static ExternalAreaModel BuildDynamicExternalArea(IReadOnlyList<Point> raw, ExternalAreaModel fallbackExternal, int screenWidth, int screenHeight)
        {
            if (raw.Count < 24) return fallbackExternal;

            int centerX = fallbackExternal.Top.X;
            int centerY = fallbackExternal.Left.Y;
            int minY = Math.Max(0, (int)Math.Round(40 * (screenHeight / 732.0)));
            int maxY = Math.Min(screenHeight - 1, (int)Math.Round(620 * (screenHeight / 732.0)));
            var filtered = raw.Where(p => p.Y >= minY && p.Y <= maxY && Math.Abs(p.X - centerX) <= screenWidth / 2).ToList();
            if (filtered.Count < 24) return fallbackExternal;

            Point left = PercentilePoint(filtered.Where(p => p.X < centerX && Math.Abs(p.Y - centerY) <= screenHeight / 5), fallbackExternal.Left, AreaVertex.LeftMost);
            Point right = PercentilePoint(filtered.Where(p => p.X > centerX && Math.Abs(p.Y - centerY) <= screenHeight / 5), fallbackExternal.Right, AreaVertex.RightMost);
            Point top = PercentilePoint(filtered.Where(p => p.Y < centerY && Math.Abs(p.X - centerX) <= screenWidth / 3), fallbackExternal.Top, AreaVertex.TopMost);
            Point bottom = PercentilePoint(filtered.Where(p => p.Y > centerY && Math.Abs(p.X - centerX) <= screenWidth / 3), fallbackExternal.Bottom, AreaVertex.BottomMost);

            if (Distance(left, right) < screenWidth * 0.25 || Distance(top, bottom) < screenHeight * 0.20) return fallbackExternal;
            return new ExternalAreaModel(left, right, top, bottom);
        }

        /// <summary>
        /// Averages the outermost tenth of a point cloud along the axis implied by <paramref name="vertex"/>.
        /// Each vertex maps to exactly one ordering; previously the right and bottom vertices were
        /// indistinguishable because both were requested with the same default flags.
        /// </summary>
        private static Point PercentilePoint(IEnumerable<Point> source, Point fallback, AreaVertex vertex)
        {
            List<Point> points = source.ToList();
            if (points.Count == 0) return fallback;

            IEnumerable<Point> ordered = vertex switch
            {
                AreaVertex.LeftMost => points.OrderBy(p => p.X),
                AreaVertex.RightMost => points.OrderByDescending(p => p.X),
                AreaVertex.TopMost => points.OrderBy(p => p.Y),
                _ => points.OrderByDescending(p => p.Y)
            };

            List<Point> sample = ordered.Take(Math.Max(3, points.Count / 10)).ToList();
            return new Point((int)Math.Round(sample.Average(p => p.X)), (int)Math.Round(sample.Average(p => p.Y)));
        }

        private static Dictionary<DropSide, List<Point>> SplitSides(IEnumerable<Point> points, ExternalAreaModel external)
        {
            var sides = Enum.GetValues<DropSide>().ToDictionary(s => s, _ => new List<Point>());
            foreach (Point p in points)
            {
                bool isLeft = p.X <= external.Top.X;
                bool isTop = p.Y <= external.Left.Y;
                DropSide side = isLeft && isTop ? DropSide.TopLeft : isLeft ? DropSide.BottomLeft : isTop ? DropSide.TopRight : DropSide.BottomRight;
                sides[side].Add(p);
            }
            return sides;
        }

        private static List<Point> CleanAndSort(List<Point> points, DropSide side)
        {
            IEnumerable<Point> ordered = side switch
            {
                DropSide.TopLeft => points.OrderBy(p => p.X).ThenByDescending(p => p.Y),
                DropSide.TopRight => points.OrderBy(p => p.X).ThenBy(p => p.Y),
                DropSide.BottomLeft => points.OrderBy(p => p.X).ThenBy(p => p.Y),
                _ => points.OrderBy(p => p.X).ThenByDescending(p => p.Y)
            };

            var clean = new List<Point>();
            foreach (Point p in ordered)
            {
                if (clean.Count > 0 && Distance(clean[^1], p) < 6) continue;
                if (clean.Count > 0 && Distance(clean[^1], p) > 110) continue;
                clean.Add(p);
            }
            return clean;
        }

        private static List<Point> MakeDropLine(IReadOnlyList<Point> searchVector, DropSide side)
        {
            if (searchVector.Count == 0) return FallbackOuterGreenLine(side);
            var line = new List<Point> { searchVector[0] };
            Point previous = searchVector[0];
            for (int idx = 1; idx < searchVector.Count; idx++)
            {
                Point current = searchVector[idx];
                int dx = current.X - previous.X;
                int dy = current.Y - previous.Y;
                int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (steps <= 0) continue;
                if (Distance(previous, current) > 75)
                {
                    line.Add(current);
                }
                else
                {
                    for (int i = 1; i <= steps; i++)
                    {
                        line.Add(new Point((int)Math.Round(previous.X + dx * i / (double)steps), (int)Math.Round(previous.Y + dy * i / (double)steps)));
                    }
                }
                previous = current;
            }
            return line;
        }

        private static List<Point> GetMbrVectorOutZone(ExternalAreaModel external, DropSide side, int screenHeight)
        {
            Point start;
            Point end;
            switch (side)
            {
                case DropSide.TopLeft:
                    start = new Point(external.Left.X + 2, external.Left.Y);
                    end = new Point(external.Top.X, external.Top.Y + 2);
                    break;
                case DropSide.TopRight:
                    start = new Point(external.Top.X, external.Top.Y + 2);
                    end = new Point(external.Right.X - 2, external.Right.Y);
                    break;
                case DropSide.BottomLeft:
                    start = new Point(external.Left.X + 2, external.Left.Y);
                    end = new Point(external.Bottom.X, external.Bottom.Y - 2);
                    break;
                default:
                    start = new Point(external.Bottom.X, external.Bottom.Y - 2);
                    end = new Point(external.Right.X - 2, external.Right.Y);
                    break;
            }

            int bottomCap = BottomCap(screenHeight);
            var vector = new List<Point>(101);
            for (int i = 0; i <= 100; i++)
            {
                int x = (int)Math.Round(start.X + ((end.X - start.X) * i) / 100.0);
                int y = (int)Math.Round(start.Y + ((end.Y - start.Y) * i) / 100.0);
                if (y > bottomCap) y = bottomCap;
                vector.Add(new Point(x, y));
            }

            return vector.Distinct().ToList();
        }

        private static List<Point> MakeDropPoints(IReadOnlyList<Point> vector, DropSide side, int pointsQty, int addTiles, Random random, int screenHeight)
        {
            if (vector.Count == 0) return new List<Point>();
            int p = Math.Max(1, vector.Count / Math.Max(1, pointsQty));
            int rndx = random.Next(0, 3);
            int rndy = random.Next(0, 3);
            var output = new List<Point>();
            for (int i = p - 1; i < vector.Count; i += p)
            {
                int start = Math.Max(0, i - p + 1);
                IReadOnlyList<Point> group = vector.Skip(start).Take(i - start + 1).ToList();
                int x = (int)Math.Round(group.Average(pt => pt.X));
                int y = (int)Math.Round(group.Average(pt => pt.Y));
                int l = addTiles * 8;
                Point adjusted = side switch
                {
                    DropSide.TopLeft => new Point(x - l - rndx, y - l - rndy),
                    DropSide.TopRight => new Point(x + l + rndx, y - l - rndy),
                    DropSide.BottomLeft => new Point(x - l - rndx, y + l + rndy),
                    _ => new Point(x + l + rndx, y + l + rndy)
                };
                output.Add(TroopDeploymentExecutor.AvoidPotionArea(ClampBottom(adjusted, screenHeight), screenHeight));
            }
            return output.Distinct().ToList();
        }

        private static Point OffsetOutside(Point pixel, DropSide side, int offset, int screenHeight)
        {
            Point result = side switch
            {
                DropSide.TopLeft => new Point((int)Math.Round(pixel.X - offset * 4 / 3.0), pixel.Y - offset),
                DropSide.BottomRight => new Point((int)Math.Round(pixel.X + offset * 4 / 3.0), pixel.Y + offset),
                DropSide.BottomLeft => new Point((int)Math.Round(pixel.X - offset * 4 / 3.0), pixel.Y + offset),
                _ => new Point((int)Math.Round(pixel.X + offset * 4 / 3.0), pixel.Y - offset)
            };
            return ClampBottom(result, screenHeight);
        }

        /// <summary>
        /// Single definition of the lowest usable drop row. Previously this bound was computed
        /// three different ways, two of which offset an MBR coordinate by a resolution delta.
        /// </summary>
        private static int BottomCap(int screenHeight) => Math.Min(screenHeight - 120, (int)Math.Round(555 * (screenHeight / 732.0)));

        private static Point ClampBottom(Point point, int screenHeight)
        {
            int cap = BottomCap(screenHeight);
            return point.Y > cap ? new Point(point.X, cap) : point;
        }

        private static List<Point> FallbackOuterGreenLine(DropSide side)
        {
            IReadOnlyList<Point> anchors = side switch
            {
                DropSide.TopLeft => BuilderBaseAttackLayout.TopLeftDrop,
                DropSide.TopRight => BuilderBaseAttackLayout.TopRightDrop,
                DropSide.BottomLeft => BuilderBaseAttackLayout.BottomLeftDrop,
                _ => BuilderBaseAttackLayout.BottomRightDrop
            };
            return anchors.ToList();
        }

        private static bool IsFurtherTroop(string troopName)
        {
            return troopName.Contains("Archer", StringComparison.OrdinalIgnoreCase)
                || troopName.Contains("Minion", StringComparison.OrdinalIgnoreCase)
                || troopName.Contains("Barbarian", StringComparison.OrdinalIgnoreCase)
                || troopName.Contains("Wizard", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSafeDropPoint(Point point, int screenWidth, int screenHeight)
        {
            if (point.X < 120 || point.X > screenWidth - 120) return false;
            if (point.Y < 70 || point.Y > screenHeight - 120) return false;

            // Avoid center-ish zone where MBR's ExternalArea would usually reject risky drops.
            // The band must stop above the potion cap: AvoidPotionArea clamps points onto that exact
            // row, so a band covering it would silently discard every clamped bottom-edge point.
            int centerBandBottom = Math.Min(700, TroopDeploymentExecutor.PotionCapY(screenHeight) - 1);
            if (point.X >= 560 && point.X <= 1040 && point.Y >= 360 && point.Y <= centerBandBottom) return false;

            return true;
        }

        private static double Distance(Point a, Point b)
        {
            int dx = a.X - b.X;
            int dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
