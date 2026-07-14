using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using VideoLibrary.Models;
using VideoLibrary.ViewModels;

namespace VideoLibrary;

public partial class MainForm : Form
{
    private readonly MainViewModel _viewModel = new();
    private readonly BindingList<VideoItem> _bindingList = new();

    public MainForm()
    {
        InitializeComponent();
        BindToViewModel();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void InitializeComponent()
    {
        Text = "Video List App";
        Width = 1200;
        Height = 675;
        StartPosition = FormStartPosition.CenterScreen;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 1,
            RowCount = 4
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topBar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };

        var pickFolderButton = new Button { Text = "Pick Folder...", Width = 120, Margin = new Padding(0, 0, 8, 0) };
        pickFolderButton.Click += async (_, _) => await _viewModel.PickFolderAsync();

        _rootFolderTextBox = new TextBox { Width = 360, ReadOnly = true, Margin = new Padding(0, 0, 8, 0) };
        var scanButton = new Button { Text = "Start Scan", Width = 100, Margin = new Padding(0, 0, 8, 0) };
        scanButton.Click += async (_, _) => await _viewModel.StartScanAsync();

        var loadLastButton = new Button { Text = "Load Last Results", Width = 150, Margin = new Padding(0, 0, 8, 0) };
        loadLastButton.Click += async (_, _) => await _viewModel.LoadLastResultsAsync();

        var loadButton = new Button { Text = "Load Previous Results", Width = 160, Margin = new Padding(0, 0, 8, 0) };
        loadButton.Click += async (_, _) => await _viewModel.LoadResultsAsync();

        var saveButton = new Button { Text = "Save Results", Width = 120, Margin = new Padding(0, 0, 8, 0) };
        saveButton.Click += (_, _) => _viewModel.SaveResults();

        topBar.Controls.Add(pickFolderButton);
        topBar.Controls.Add(_rootFolderTextBox);
        topBar.Controls.Add(scanButton);
        topBar.Controls.Add(loadLastButton);
        topBar.Controls.Add(loadButton);
        topBar.Controls.Add(saveButton);

        var filterBar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };

        _filterTextBox = new TextBox { Width = 300, Margin = new Padding(0, 0, 8, 0) };
        _filterTextBox.TextChanged += (_, _) => _viewModel.FilterText = _filterTextBox.Text;
        var applyFilterButton = new Button { Text = "Apply Filter", Width = 100, Margin = new Padding(0, 0, 8, 0) };
        applyFilterButton.Click += (_, _) => _viewModel.ApplyFilter();
        filterBar.Controls.Add(_filterTextBox);
        filterBar.Controls.Add(applyFilterButton);

        _progressBar = new ProgressBar { Style = ProgressBarStyle.Continuous, Width = 300, Height = 16, Minimum = 0, Maximum = 100, Visible = false };
        _statusLabel = new Label { AutoSize = true, ForeColor = System.Drawing.Color.Gray, Margin = new Padding(0, 4, 0, 0) };

        _videoGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };
        _videoGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Filename", DataPropertyName = "FileName", Width = 180 });
        _videoGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Folder", DataPropertyName = "FolderPath", Width = 320 });
        _videoGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Size", DataPropertyName = "SizeBytes", Width = 120 });
        _videoGrid.CellDoubleClick += (_, _) =>
        {
            if (_videoGrid.CurrentRow?.DataBoundItem is VideoItem item)
            {
                _viewModel.OpenItem(item);
            }
        };
        _videoGrid.DataSource = _bindingList;

        panel.Controls.Add(topBar, 0, 0);
        panel.Controls.Add(filterBar, 0, 1);
        panel.Controls.Add(_progressBar, 0, 2);
        panel.Controls.Add(_statusLabel, 0, 2);
        panel.Controls.Add(_videoGrid, 0, 3);
        Controls.Add(panel);
    }

    private void ConfigureUi()
    {
        Text = "Video List App";
        Width = 1200;
        Height = 675;
        StartPosition = FormStartPosition.CenterScreen;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 1,
            RowCount = 4
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topBar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };

        var pickFolderButton = new Button { Text = "Pick Folder...", Width = 120, Margin = new Padding(0, 0, 8, 0) };
        pickFolderButton.Click += async (_, _) => await _viewModel.PickFolderAsync();

        _rootFolderTextBox = new TextBox { Width = 360, ReadOnly = true, Margin = new Padding(0, 0, 8, 0) };
        var scanButton = new Button { Text = "Start Scan", Width = 100, Margin = new Padding(0, 0, 8, 0) };
        scanButton.Click += async (_, _) => await _viewModel.StartScanAsync();

        var loadLastButton = new Button { Text = "Load Last Results", Width = 150, Margin = new Padding(0, 0, 8, 0) };
        loadLastButton.Click += async (_, _) => await _viewModel.LoadLastResultsAsync();

        var loadButton = new Button { Text = "Load Previous Results", Width = 160, Margin = new Padding(0, 0, 8, 0) };
        loadButton.Click += async (_, _) => await _viewModel.LoadResultsAsync();

        var saveButton = new Button { Text = "Save Results", Width = 120, Margin = new Padding(0, 0, 8, 0) };
        saveButton.Click += (_, _) => _viewModel.SaveResults();

        topBar.Controls.Add(pickFolderButton);
        topBar.Controls.Add(_rootFolderTextBox);
        topBar.Controls.Add(scanButton);
        topBar.Controls.Add(loadLastButton);
        topBar.Controls.Add(loadButton);
        topBar.Controls.Add(saveButton);

        var filterBar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };

        _filterTextBox = new TextBox { Width = 300, Margin = new Padding(0, 0, 8, 0) };
        _filterTextBox.TextChanged += (_, _) => _viewModel.FilterText = _filterTextBox.Text;
        var applyFilterButton = new Button { Text = "Apply Filter", Width = 100, Margin = new Padding(0, 0, 8, 0) };
        applyFilterButton.Click += (_, _) => _viewModel.ApplyFilter();
        filterBar.Controls.Add(_filterTextBox);
        filterBar.Controls.Add(applyFilterButton);

        _progressBar = new ProgressBar { Style = ProgressBarStyle.Continuous, Width = 300, Height = 16, Minimum = 0, Maximum = 100, Visible = false };
        _statusLabel = new Label { AutoSize = true, ForeColor = System.Drawing.Color.Gray, Margin = new Padding(0, 4, 0, 0) };

        _videoGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false
        };
        _videoGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Filename", DataPropertyName = "FileName", Width = 180 });
        _videoGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Folder", DataPropertyName = "FolderPath", Width = 320 });
        _videoGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Size", DataPropertyName = "SizeBytes", Width = 120 });
        _videoGrid.CellDoubleClick += (_, _) =>
        {
            if (_videoGrid.CurrentRow?.DataBoundItem is VideoItem item)
            {
                _viewModel.OpenItem(item);
            }
        };
        _videoGrid.DataSource = _bindingList;

        panel.Controls.Add(topBar, 0, 0);
        panel.Controls.Add(filterBar, 0, 1);
        panel.Controls.Add(_progressBar, 0, 2);
        panel.Controls.Add(_statusLabel, 0, 2);
        panel.Controls.Add(_videoGrid, 0, 3);
        Controls.Add(panel);
    }

    private void BindToViewModel()
    {
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.RootFolder))
            {
                _rootFolderTextBox.Text = _viewModel.RootFolder;
            }
            else if (e.PropertyName == nameof(MainViewModel.FilterText))
            {
                if (_filterTextBox.Text != _viewModel.FilterText)
                {
                    _filterTextBox.Text = _viewModel.FilterText;
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.StatusText))
            {
                _statusLabel.Text = _viewModel.StatusText;
            }
            else if (e.PropertyName == nameof(MainViewModel.Progress))
            {
                _progressBar.Value = (int)Math.Round(_viewModel.Progress);
            }
            else if (e.PropertyName == nameof(MainViewModel.IsBusy))
            {
                _progressBar.Visible = _viewModel.IsBusy;
                _progressBar.Style = ProgressBarStyle.Marquee;
            }
        };
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Items) || e.PropertyName == nameof(MainViewModel.ItemsChanged))
        {
            RefreshGrid();
        }
    }

    private void RefreshGrid()
    {
        _bindingList.Clear();
        foreach (var item in _viewModel.Items)
        {
            _bindingList.Add(item);
        }
    }

    private TextBox _rootFolderTextBox = null!;
    private TextBox _filterTextBox = null!;
    private ProgressBar _progressBar = null!;
    private Label _statusLabel = null!;
    private DataGridView _videoGrid = null!;
}
