using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Represents a single quest node in the visual DAG flowchart.
    /// </summary>
    public class QuestGraphNode : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isHighlighted;
        private bool _isDimmed;
        private bool _matchesSearch = true;
        private QuestStatus _status;

        public TarkovTask Task { get; set; } = null!;
        public string Id => Task?.Id ?? string.Empty;
        public string NormalizedName => Task?.NormalizedName ?? string.Empty;
        public string Name => Task?.Name ?? string.Empty;
        public string Trader => Task?.Trader ?? string.Empty;
        public int? RequiredLevel => Task?.RequiredLevel;
        public string Location => (Task?.Maps != null && Task.Maps.Count > 0) ? string.Join(", ", Task.Maps) : "Any Map";
        public bool ReqKappa => Task?.ReqKappa ?? false;

        public QuestStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(StatusBrush));
                    OnPropertyChanged(nameof(StatusBorderBrush));
                    OnPropertyChanged(nameof(StatusBackgroundBrush));
                    OnPropertyChanged(nameof(StatusBadgeText));
                    OnPropertyChanged(nameof(IsDone));
                    OnPropertyChanged(nameof(IsActive));
                    OnPropertyChanged(nameof(IsLocked));
                }
            }
        }

        public int Depth { get; set; }
        public int Row { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 220;
        public double Height { get; set; } = 72;

        public Point LeftPort => new Point(X, Y + Height / 2.0);
        public Point RightPort => new Point(X + Width, Y + Height / 2.0);

        public List<string> DirectPrerequisites { get; set; } = new();
        public List<string> DirectFollowUps { get; set; } = new();
        public List<CrossTraderPrerequisite> CrossTraderPrerequisites { get; set; } = new();

        public bool HasCrossTraderPrereqs => CrossTraderPrerequisites.Count > 0;
        public bool HasLevelReq => RequiredLevel.HasValue && RequiredLevel.Value > 1;
        public string LevelBadgeText => HasLevelReq ? $"Lvl {RequiredLevel}" : string.Empty;

        public bool IsDone => Status == QuestStatus.Done;
        public bool IsActive => Status == QuestStatus.Active;
        public bool IsLocked => Status == QuestStatus.Locked || Status == QuestStatus.LevelLocked || Status == QuestStatus.Unavailable;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                    OnPropertyChanged(nameof(CardBorderBrush));
                }
            }
        }

        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted != value)
                {
                    _isHighlighted = value;
                    OnPropertyChanged(nameof(IsHighlighted));
                    OnPropertyChanged(nameof(CardBorderBrush));
                    OnPropertyChanged(nameof(CardOpacity));
                }
            }
        }

        public bool IsDimmed
        {
            get => _isDimmed;
            set
            {
                if (_isDimmed != value)
                {
                    _isDimmed = value;
                    OnPropertyChanged(nameof(IsDimmed));
                    OnPropertyChanged(nameof(CardOpacity));
                }
            }
        }

        public bool MatchesSearch
        {
            get => _matchesSearch;
            set
            {
                if (_matchesSearch != value)
                {
                    _matchesSearch = value;
                    OnPropertyChanged(nameof(MatchesSearch));
                    OnPropertyChanged(nameof(CardOpacity));
                }
            }
        }

        public double CardOpacity
        {
            get
            {
                if (!MatchesSearch) return 0.20;
                if (IsDimmed) return 0.25;
                if (IsLocked) return 0.65;
                return 1.0;
            }
        }

        public Brush CardBorderBrush
        {
            get
            {
                if (IsSelected) return BrushFromHex("#E5A93C"); // Tactical Gold Glow
                if (IsHighlighted) return BrushFromHex("#FFD54F");
                if (IsDone) return BrushFromHex("#2E7D32"); // Forest Green
                if (IsActive) return BrushFromHex("#C5A84A"); // Available Gold
                return BrushFromHex("#2A2C34"); // Subtle Dark Border
            }
        }

        public Brush StatusBorderBrush => Status switch
        {
            QuestStatus.Done => BrushFromHex("#388E3C"),
            QuestStatus.Active => BrushFromHex("#C5A84A"),
            QuestStatus.Failed => BrushFromHex("#D32F2F"),
            QuestStatus.LevelLocked => BrushFromHex("#8D6E63"),
            _ => BrushFromHex("#3A3D48")
        };

        public Brush StatusBackgroundBrush => Status switch
        {
            QuestStatus.Done => BrushFromHex("#142A16"),
            QuestStatus.Active => BrushFromHex("#2B2412"),
            QuestStatus.Failed => BrushFromHex("#2A1414"),
            QuestStatus.LevelLocked => BrushFromHex("#241D17"),
            _ => BrushFromHex("#16181F")
        };

        public Brush StatusBrush => Status switch
        {
            QuestStatus.Done => BrushFromHex("#4CAF50"),
            QuestStatus.Active => BrushFromHex("#FFC107"),
            QuestStatus.Failed => BrushFromHex("#EF5350"),
            QuestStatus.LevelLocked => BrushFromHex("#BCAAA4"),
            _ => BrushFromHex("#787C8A")
        };

        public string StatusBadgeText => Status switch
        {
            QuestStatus.Done => "DONE",
            QuestStatus.Active => "READY",
            QuestStatus.Failed => "FAILED",
            QuestStatus.LevelLocked => "LVL LOCK",
            _ => "LOCKED"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private static readonly Dictionary<string, SolidColorBrush> _brushCache = new();
        private static SolidColorBrush BrushFromHex(string hex)
        {
            if (_brushCache.TryGetValue(hex, out var brush)) return brush;
            var color = (Color)ColorConverter.ConvertFromString(hex);
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushCache[hex] = brush;
            return brush;
        }
    }

    /// <summary>
    /// Prerequisite that belongs to another trader, displayed as a compact interactive pill on the card.
    /// </summary>
    public class CrossTraderPrerequisite
    {
        public string Trader { get; set; } = string.Empty;
        public string QuestName { get; set; } = string.Empty;
        public string NormalizedName { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public string DisplayBadge => $"{Trader}: {QuestName}";
    }

    /// <summary>
    /// Directed connection line (cubic Bézier curve) between a parent quest and child quest.
    /// </summary>
    public class QuestGraphEdge : INotifyPropertyChanged
    {
        private bool _isHighlighted;
        private bool _isDimmed;

        public QuestGraphNode SourceNode { get; set; } = null!;
        public QuestGraphNode TargetNode { get; set; } = null!;
        public Geometry Geometry { get; set; } = null!;

        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted != value)
                {
                    _isHighlighted = value;
                    OnPropertyChanged(nameof(IsHighlighted));
                    OnPropertyChanged(nameof(StrokeBrush));
                    OnPropertyChanged(nameof(StrokeThickness));
                    OnPropertyChanged(nameof(Opacity));
                }
            }
        }

        public bool IsDimmed
        {
            get => _isDimmed;
            set
            {
                if (_isDimmed != value)
                {
                    _isDimmed = value;
                    OnPropertyChanged(nameof(IsDimmed));
                    OnPropertyChanged(nameof(StrokeBrush));
                    OnPropertyChanged(nameof(Opacity));
                }
            }
        }

        public double Opacity => IsDimmed ? 0.15 : (IsHighlighted ? 1.0 : 0.65);
        public double StrokeThickness => IsHighlighted ? 2.5 : 1.5;

        public Brush StrokeBrush
        {
            get
            {
                if (IsHighlighted) return Brushes.Gold;
                if (SourceNode.IsDone) return BrushFromHex("#388E3C"); // Completed flow
                return BrushFromHex("#454A59"); // Default tactical line
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private static readonly Dictionary<string, SolidColorBrush> _brushCache = new();
        private static SolidColorBrush BrushFromHex(string hex)
        {
            if (_brushCache.TryGetValue(hex, out var brush)) return brush;
            var color = (Color)ColorConverter.ConvertFromString(hex);
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushCache[hex] = brush;
            return brush;
        }
    }

    /// <summary>
    /// Encapsulates the computed layout dimensions, nodes, and edges for visual rendering.
    /// </summary>
    public class QuestGraphLayoutResult
    {
        public string ModeTitle { get; set; } = string.Empty;
        public int TotalQuests { get; set; }
        public int CompletedQuests { get; set; }
        public int AvailableQuests { get; set; }
        public int LockedQuests { get; set; }
        public double TotalWidth { get; set; }
        public double TotalHeight { get; set; }
        public List<QuestGraphNode> Nodes { get; set; } = new();
        public List<QuestGraphEdge> Edges { get; set; } = new();
    }

    /// <summary>
    /// Topologically layered DAG layout generator for Escape from Tarkov quests.
    /// Partitions by Trader or Kappa Path, computes horizontal depth layers, minimizes edge crossings,
    /// and builds smooth cubic Bézier geometries.
    /// </summary>
    public static class QuestGraphLayoutEngine
    {
        public const double NodeWidth = 220;
        public const double NodeHeight = 72;
        public const double ColumnPitch = 330; // 220 + 110 gap
        public const double RowPitch = 98;     // 72 + 26 gap
        public const double MarginX = 60;
        public const double MarginY = 60;

        /// <summary>
        /// Builds the complete layout for a given trader or "Kappa".
        /// </summary>
        public static QuestGraphLayoutResult BuildLayout(
            IEnumerable<TarkovTask> allTasks,
            string targetMode,
            Func<string, QuestStatus> getStatusFunc,
            string? searchQuery = null)
        {
            var taskList = allTasks.ToList();
            var taskLookup = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in taskList)
            {
                if (!string.IsNullOrEmpty(t.NormalizedName))
                    taskLookup[t.NormalizedName] = t;
                if (!string.IsNullOrEmpty(t.Id))
                    taskLookup[t.Id] = t;
            }

            // 1. Filter tasks according to target mode
            bool isKappaMode = string.Equals(targetMode, "Kappa", StringComparison.OrdinalIgnoreCase);
            List<TarkovTask> groupTasks;

            if (isKappaMode)
            {
                // Kappa Path: All reqKappa quests and their prerequisites
                var kappaTasks = taskList.Where(t => t.ReqKappa).ToList();
                var kappaSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kt in kappaTasks)
                {
                    if (!string.IsNullOrEmpty(kt.NormalizedName)) kappaSet.Add(kt.NormalizedName);
                    if (kt.Previous != null)
                    {
                        var prereqs = QuestGraphService.Instance.GetAllPrerequisites(kt.NormalizedName ?? kt.Id);
                        foreach (var p in prereqs)
                        {
                            if (!string.IsNullOrEmpty(p.NormalizedName)) kappaSet.Add(p.NormalizedName);
                        }
                    }
                }
                groupTasks = taskList.Where(t => !string.IsNullOrEmpty(t.NormalizedName) && kappaSet.Contains(t.NormalizedName)).ToList();
            }
            else
            {
                // Standard Trader partition (e.g. Prapor, Therapist, etc.)
                groupTasks = taskList
                    .Where(t => string.Equals(t.Trader, targetMode, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var groupNorms = new HashSet<string>(groupTasks.Select(t => t.NormalizedName ?? "").Where(s => !string.IsNullOrEmpty(s)), StringComparer.OrdinalIgnoreCase);

            // 2. Initialize node models
            var nodes = new List<QuestGraphNode>();
            var nodeLookup = new Dictionary<string, QuestGraphNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in groupTasks)
            {
                var norm = task.NormalizedName ?? task.Id;
                var status = getStatusFunc(norm);

                var node = new QuestGraphNode
                {
                    Task = task,
                    Status = status,
                    Width = NodeWidth,
                    Height = NodeHeight
                };

                // Add direct intra-group prerequisites
                if (task.Previous != null)
                {
                    foreach (var prevNorm in task.Previous)
                    {
                        if (groupNorms.Contains(prevNorm))
                        {
                            node.DirectPrerequisites.Add(prevNorm);
                        }
                        else
                        {
                            // Cross-trader prerequisite
                            if (taskLookup.TryGetValue(prevNorm, out var extTask))
                            {
                                bool extCompleted = getStatusFunc(prevNorm) == QuestStatus.Done;
                                node.CrossTraderPrerequisites.Add(new CrossTraderPrerequisite
                                {
                                    Trader = extTask.Trader ?? "Other",
                                    QuestName = extTask.Name ?? prevNorm,
                                    NormalizedName = prevNorm,
                                    IsCompleted = extCompleted
                                });
                            }
                        }
                    }
                }

                // Add direct intra-group follow-ups
                if (task.LeadsTo != null)
                {
                    foreach (var nextNorm in task.LeadsTo)
                    {
                        if (groupNorms.Contains(nextNorm))
                        {
                            node.DirectFollowUps.Add(nextNorm);
                        }
                    }
                }

                nodes.Add(node);
                nodeLookup[norm] = node;
            }

            // 3. Compute topological depth (Columns)
            // Longest path in DAG from any starter quest within this group
            var depthCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int GetDepth(string norm)
            {
                if (depthCache.TryGetValue(norm, out int d)) return d;
                if (!nodeLookup.TryGetValue(norm, out var n)) return 0;
                if (visiting.Contains(norm)) return 0; // Avoid circular dependency loops

                visiting.Add(norm);
                int maxParentDepth = -1;

                foreach (var parentNorm in n.DirectPrerequisites)
                {
                    int pd = GetDepth(parentNorm);
                    if (pd > maxParentDepth) maxParentDepth = pd;
                }

                visiting.Remove(norm);
                int result = maxParentDepth + 1;
                depthCache[norm] = result;
                return result;
            }

            foreach (var node in nodes)
            {
                node.Depth = GetDepth(node.NormalizedName);
            }

            // 4. Distribute nodes into rows within each column (Barycenter crossing reduction)
            var columns = nodes.GroupBy(n => n.Depth).OrderBy(g => g.Key).ToList();
            int maxDepth = columns.Count > 0 ? columns.Max(c => c.Key) : 0;
            int maxRowCount = 0;

            foreach (var colGroup in columns)
            {
                var colNodes = colGroup.ToList();
                int colIndex = colGroup.Key;

                if (colIndex == 0)
                {
                    // Sort starters by minimum level requirement, then name
                    colNodes = colNodes
                        .OrderBy(n => n.RequiredLevel ?? 0)
                        .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                else
                {
                    // Sort by average row position of direct parents
                    colNodes = colNodes
                        .OrderBy(n =>
                        {
                            var parentRows = n.DirectPrerequisites
                                .Where(p => nodeLookup.ContainsKey(p))
                                .Select(p => nodeLookup[p].Row)
                                .ToList();
                            return parentRows.Count > 0 ? parentRows.Average() : (n.RequiredLevel ?? 0);
                        })
                        .ThenBy(n => n.RequiredLevel ?? 0)
                        .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                for (int r = 0; r < colNodes.Count; r++)
                {
                    var node = colNodes[r];
                    node.Row = r;
                    node.X = MarginX + node.Depth * ColumnPitch;
                    node.Y = MarginY + node.Row * RowPitch;
                }

                if (colNodes.Count > maxRowCount)
                    maxRowCount = colNodes.Count;
            }

            // 5. Generate edges and Bézier path geometries
            var edges = new List<QuestGraphEdge>();
            var edgeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var targetNode in nodes)
            {
                foreach (var parentNorm in targetNode.DirectPrerequisites)
                {
                    if (nodeLookup.TryGetValue(parentNorm, out var sourceNode))
                    {
                        var edgeKey = $"{sourceNode.NormalizedName}->{targetNode.NormalizedName}";
                        if (edgeSet.Contains(edgeKey)) continue;
                        edgeSet.Add(edgeKey);

                        var geom = CreateSmoothBezierGeometry(sourceNode.RightPort, targetNode.LeftPort);
                        var edge = new QuestGraphEdge
                        {
                            SourceNode = sourceNode,
                            TargetNode = targetNode,
                            Geometry = geom
                        };
                        edges.Add(edge);
                    }
                }
            }

            // 6. Apply search filter highlights if provided
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var query = searchQuery.Trim();
                foreach (var node in nodes)
                {
                    bool match = node.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                 node.Location.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                 node.Trader.Contains(query, StringComparison.OrdinalIgnoreCase);
                    node.MatchesSearch = match;
                }
            }
            else
            {
                foreach (var node in nodes)
                {
                    node.MatchesSearch = true;
                }
            }

            // 7. Calculate total canvas dimensions
            double totalWidth = MarginX * 2 + (maxDepth + 1) * ColumnPitch;
            double totalHeight = MarginY * 2 + Math.Max(maxRowCount, 1) * RowPitch;

            return new QuestGraphLayoutResult
            {
                ModeTitle = isKappaMode ? "Collector / Kappa Quest Tree" : $"{targetMode} Quests",
                TotalQuests = nodes.Count,
                CompletedQuests = nodes.Count(n => n.IsDone),
                AvailableQuests = nodes.Count(n => n.IsActive),
                LockedQuests = nodes.Count(n => n.IsLocked),
                TotalWidth = Math.Max(totalWidth, 1200),
                TotalHeight = Math.Max(totalHeight, 700),
                Nodes = nodes,
                Edges = edges
            };
        }

        /// <summary>
        /// Generates a smooth cubic Bézier curve with an integrated directional arrow marker.
        /// </summary>
        private static Geometry CreateSmoothBezierGeometry(Point p1, Point p2)
        {
            double dx = Math.Abs(p2.X - p1.X) * 0.5;
            if (dx < 30) dx = 30;

            var controlPoint1 = new Point(p1.X + dx, p1.Y);
            var controlPoint2 = new Point(p2.X - dx, p2.Y);

            var figure = new PathFigure
            {
                StartPoint = p1,
                IsClosed = false,
                IsFilled = false
            };

            var bezier = new BezierSegment(controlPoint1, controlPoint2, p2, true);
            figure.Segments.Add(bezier);

            // Arrow marker pointing right towards target left port
            const double arrowSize = 5.0;
            var arrowTop = new Point(p2.X - arrowSize * 1.6, p2.Y - arrowSize);
            var arrowBottom = new Point(p2.X - arrowSize * 1.6, p2.Y + arrowSize);

            var arrowFigure = new PathFigure
            {
                StartPoint = arrowTop,
                IsClosed = true,
                IsFilled = true
            };
            arrowFigure.Segments.Add(new LineSegment(p2, true));
            arrowFigure.Segments.Add(new LineSegment(arrowBottom, true));

            var geometryGroup = new PathGeometry();
            geometryGroup.Figures.Add(figure);
            geometryGroup.Figures.Add(arrowFigure);
            geometryGroup.Freeze();

            return geometryGroup;
        }
    }
}
