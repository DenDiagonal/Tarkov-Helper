using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TarkovHelper.Services;

namespace TarkovHelper.Controls
{
    public partial class QuestGraphNodeControl : UserControl
    {
        public static readonly RoutedEvent NodeClickedEvent =
            EventManager.RegisterRoutedEvent(nameof(NodeClicked), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(QuestGraphNodeControl));

        public event RoutedEventHandler NodeClicked
        {
            add => AddHandler(NodeClickedEvent, value);
            remove => RemoveHandler(NodeClickedEvent, value);
        }

        public static readonly RoutedEvent NodeHoverChangedEvent =
            EventManager.RegisterRoutedEvent(nameof(NodeHoverChanged), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(QuestGraphNodeControl));

        public event RoutedEventHandler NodeHoverChanged
        {
            add => AddHandler(NodeHoverChangedEvent, value);
            remove => RemoveHandler(NodeHoverChangedEvent, value);
        }

        public static readonly RoutedEvent CrossTraderClickedEvent =
            EventManager.RegisterRoutedEvent(nameof(CrossTraderClicked), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(QuestGraphNodeControl));

        public event RoutedEventHandler CrossTraderClicked
        {
            add => AddHandler(CrossTraderClickedEvent, value);
            remove => RemoveHandler(CrossTraderClickedEvent, value);
        }

        public QuestGraphNode? Node => DataContext as QuestGraphNode;

        public QuestGraphNodeControl()
        {
            InitializeComponent();
        }

        private void UserControl_MouseEnter(object sender, MouseEventArgs e)
        {
            RaiseEvent(new RoutedEventArgs(NodeHoverChangedEvent, this));
        }

        private void UserControl_MouseLeave(object sender, MouseEventArgs e)
        {
            RaiseEvent(new RoutedEventArgs(NodeHoverChangedEvent, null));
        }

        private void UserControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            RaiseEvent(new RoutedEventArgs(NodeClickedEvent, this));
        }

        private void CrossTraderBadge_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is FrameworkElement fe && fe.DataContext is CrossTraderPrerequisite req)
            {
                var args = new RoutedEventArgs(CrossTraderClickedEvent, req);
                RaiseEvent(args);
            }
        }
    }
}
