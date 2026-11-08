using System;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace TarkovHelper.Controls
{
    /// <summary>
    /// A UniformGrid that dynamically adjusts its column count based on available width
    /// and a minimum item width threshold, creating a responsive multi-column layout.
    /// </summary>
    public class AdaptiveUniformGrid : UniformGrid
    {
        public static readonly DependencyProperty MinItemWidthProperty =
            DependencyProperty.Register(
                nameof(MinItemWidth),
                typeof(double),
                typeof(AdaptiveUniformGrid),
                new FrameworkPropertyMetadata(130.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double MinItemWidth
        {
            get => (double)GetValue(MinItemWidthProperty);
            set => SetValue(MinItemWidthProperty, value);
        }

        public static readonly DependencyProperty MaxColumnsProperty =
            DependencyProperty.Register(
                nameof(MaxColumns),
                typeof(int),
                typeof(AdaptiveUniformGrid),
                new FrameworkPropertyMetadata(6, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public int MaxColumns
        {
            get => (int)GetValue(MaxColumnsProperty);
            set => SetValue(MaxColumnsProperty, value);
        }

        public static readonly DependencyProperty MinColumnsProperty =
            DependencyProperty.Register(
                nameof(MinColumns),
                typeof(int),
                typeof(AdaptiveUniformGrid),
                new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public int MinColumns
        {
            get => (int)GetValue(MinColumnsProperty);
            set => SetValue(MinColumnsProperty, value);
        }

        protected override Size MeasureOverride(Size constraint)
        {
            if (!double.IsInfinity(constraint.Width) && constraint.Width > 0 && MinItemWidth > 0)
            {
                int calculatedCols = (int)Math.Floor(constraint.Width / MinItemWidth);
                int clampedCols = Math.Clamp(calculatedCols, MinColumns, MaxColumns);
                if (Columns != clampedCols)
                {
                    Columns = clampedCols;
                }
            }

            return base.MeasureOverride(constraint);
        }
    }
}
