using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace IMLCGui
{
    /// <summary>
    /// Interaction logic for LatencyRow.xaml
    /// </summary>
    public partial class LatencyRow : UserControl, INotifyPropertyChanged
    {
        public bool ShowGridLines
        {
            get { return (bool)GetValue(ShowGridLinesProperty); }
            set
            {
                SetValue(ShowGridLinesProperty, value);
            }
        }
        public static readonly DependencyProperty ShowGridLinesProperty = DependencyProperty.Register(
            "ShowGridLinesProperty",
            typeof(bool),
            typeof(LatencyRow),
            new PropertyMetadata(false)
        );

        public string InjectDelay
        {
            get { return (string)GetValue(InjectDelayProperty); }
            set
            {
                SetValue(InjectDelayProperty, value);
                OnPropertyChanged();
            }
        }

        public static readonly DependencyProperty InjectDelayProperty = DependencyProperty.Register(
            "InjectDelayProperty",
            typeof(string),
            typeof(LatencyRow),
            new PropertyMetadata("")
        );

        public string Latency
        {
            get { return (string)GetValue(LatencyProperty); }
            set
            {
                SetValue(LatencyProperty, value);
                OnPropertyChanged();
            }
        }
        public static readonly DependencyProperty LatencyProperty = DependencyProperty.Register(
            "LatencyProperty",
            typeof(string),
            typeof(LatencyRow),
            new PropertyMetadata("")
        );

        public string Bandwidth
        {
            get { return (string)GetValue(BandwidthProperty); }
            set
            {
                SetValue(BandwidthProperty, value);
                OnPropertyChanged();
            }
        }
        public static readonly DependencyProperty BandwidthProperty = DependencyProperty.Register(
            "BandwidthProperty",
            typeof(string),
            typeof(LatencyRow),
            new PropertyMetadata("")
        );

        public event PropertyChangedEventHandler PropertyChanged;

        public LatencyRow()
        {
            InitializeComponent();
        }

        // Create the OnPropertyChanged method to raise the event
        // The calling member's name will be used as the parameter.
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
