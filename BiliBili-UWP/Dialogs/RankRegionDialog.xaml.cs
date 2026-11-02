using BiliBili_Lib.Models.BiliBili;
using BiliBili_UWP.Models.Enums;
using BiliBili_UWP.Models.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace BiliBili_UWP.Dialogs
{
    public sealed partial class RankRegionDialog : ContentDialog
    {
        public ObservableCollection<RankRegionOption> Options { get; } = new ObservableCollection<RankRegionOption>();
        public List<int> SelectedIds => Options.Where(p => p.IsSelected).Select(p => p.Tid).ToList();

        public RankRegionDialog(IEnumerable<RegionContainer> regions, IEnumerable<int> selectedIds)
        {
            this.InitializeComponent();
            var selected = new HashSet<int>(selectedIds ?? Enumerable.Empty<int>());
            foreach (var region in regions ?? Enumerable.Empty<RegionContainer>())
            {
                Options.Add(new RankRegionOption(region, selected.Contains(region.tid)));
            }
            UpdateSelectionState();
        }

        private void RegionTile_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var option = (sender as FrameworkElement)?.DataContext as RankRegionOption;
            if (option == null)
                return;
            option.IsSelected = !option.IsSelected;
            UpdateSelectionState();
            e.Handled = true;
        }

        private void UpdateSelectionState()
        {
            int count = Options.Count(p => p.IsSelected);
            CountBlock.Text = $"已选 {count} 个";
            HintBlock.Text = count == 0
                ? "至少选择一个分区后才能保存"
                : "选择要在排行榜顶部显示的分区，至少保留一个";
            IsPrimaryButtonEnabled = count > 0;
        }
    }

    public class RankRegionOption : INotifyPropertyChanged
    {
        private bool _isSelected;
        private readonly Brush _normalBackground = UIHelper.GetThemeBrush(ColorType.CardBackground);
        private readonly Brush _selectedBackground = UIHelper.GetThemeBrush(ColorType.PrimaryColorOpacity);
        private readonly Brush _normalBorder = UIHelper.GetThemeBrush(ColorType.LineColor);
        private readonly Brush _selectedBorder = UIHelper.GetThemeBrush(ColorType.PrimaryColor);

        public RankRegionOption(RegionContainer region, bool isSelected)
        {
            Tid = region.tid;
            Name = string.IsNullOrEmpty(region.name) ? "未命名分区" : region.name;
            _isSelected = isSelected;
            string logo = string.IsNullOrEmpty(region.logo) ? "ms-appx:///Assets/logo_small.png" : region.logo;
            try
            {
                LogoBrush = new ImageBrush
                {
                    ImageSource = new BitmapImage(new Uri(logo)) { DecodePixelWidth = 88 },
                    Stretch = Stretch.UniformToFill
                };
            }
            catch
            {
                LogoBrush = new ImageBrush
                {
                    ImageSource = new BitmapImage(new Uri("ms-appx:///Assets/logo_small.png")),
                    Stretch = Stretch.UniformToFill
                };
            }
        }

        public int Tid { get; }
        public string Name { get; }
        public ImageBrush LogoBrush { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
                OnPropertyChanged(nameof(CheckVisibility));
                OnPropertyChanged(nameof(TileBackground));
                OnPropertyChanged(nameof(TileBorderBrush));
            }
        }

        public Visibility CheckVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
        public Brush TileBackground => IsSelected ? _selectedBackground : _normalBackground;
        public Brush TileBorderBrush => IsSelected ? _selectedBorder : _normalBorder;

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
