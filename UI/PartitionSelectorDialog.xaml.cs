// Licensed to Kethily Daniel & NDXCode under one or more agreements.
// Kethily Daniel & NDXCode licenses this file to you under the Business Source License 1.1.
// See the LICENSE file in the project root for more information.

/*
 * Copyright (c) 2026 Kethily Daniel & NDXCode. All rights reserved.
 * 
 * Use of this software is governed by the Business Source License included 
 * in the LICENSE file and at www.mariadb.com/bsl11.
 * 
 * Change Date: Four years from the date each version of the Licensed Work 
 * is first publicly distributed.
 * 
 * On the Change Date, in accordance with the Business Source License, 
 * use of this software will be governed by the GNU General Public License v3.0 
 * or later (GPL-3.0-or-later).
 * 
 * Contact Information: https://kirintool.cfd
 */

using Kirin_Tool.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Wpf.Ui.Controls;

namespace Kirin_Tool.UI
{
    public partial class PartitionSelectorDialog : ContentDialog, INotifyPropertyChanged
    {
        private ObservableCollection<PartitionInfo> _partitions;
        private string _statusText;
        private bool _updatingSelectAll = false;
        private bool? _selectAllState = false;

        public ObservableCollection<PartitionInfo> Partitions
        {
            get => _partitions;
            set
            {
                _partitions = value;
                OnPropertyChanged(nameof(Partitions));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public bool HasSelectedPartitions => Partitions?.Any(p => p.IsSelected) == true;

        public bool? SelectAllState
        {
            get => _selectAllState;
            set
            {
                if (_selectAllState != value)
                {
                    _selectAllState = value;
                    OnPropertyChanged(nameof(SelectAllState));

                    if (!_updatingSelectAll && value.HasValue)
                    {
                        ApplySelectAllState(value.Value);
                    }
                }
            }
        }

        public List<PartitionInfo> SelectedPartitions { get; private set; }
        public bool DialogResult { get; private set; }

        public PartitionSelectorDialog(List<PartitionInfo> partitions)
        {
            InitializeComponent();

            Partitions = new ObservableCollection<PartitionInfo>(partitions ?? new List<PartitionInfo>());

            foreach (var partition in Partitions)
            {
                partition.PropertyChanged += OnPartitionPropertyChanged;
            }

            DataContext = this;
            UpdateStatusText();
            UpdateSelectAllState();

            this.Closed += OnDialogClosed;
        }

        private void OnPartitionPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PartitionInfo.IsSelected))
            {
                if (!_updatingSelectAll)
                {
                    OnPropertyChanged(nameof(HasSelectedPartitions));
                    UpdateSelectAllState();
                    UpdateStatusText();
                }
            }
        }

        private void UpdateSelectAllState()
        {
            if (Partitions == null || !Partitions.Any())
            {
                SelectAllState = false;
                return;
            }

            var selectedCount = Partitions.Count(p => p.IsSelected);

            if (selectedCount == 0)
                SelectAllState = false;
            else if (selectedCount == Partitions.Count)
                SelectAllState = true;
            else
                SelectAllState = null;
        }

        private void ApplySelectAllState(bool selectAll)
        {
            _updatingSelectAll = true;

            try
            {
                foreach (var partition in Partitions)
                {
                    partition.IsSelected = selectAll;
                }
            }
            finally
            {
                _updatingSelectAll = false;
            }

            OnPropertyChanged(nameof(HasSelectedPartitions));
            UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            if (Partitions == null)
            {
                StatusText = "No partitions available";
                return;
            }

            var selectedCount = Partitions.Count(p => p.IsSelected);
            var totalCount = Partitions.Count;

            if (selectedCount == 0)
            {
                StatusText = $"{totalCount} partitions available";
            }
            else
            {
                var totalSize = Partitions.Where(p => p.IsSelected).Sum(p => (long)p.Size);
                StatusText = $"{selectedCount} of {totalCount} partitions selected ({FormatBytes(totalSize)})";
            }
        }

        private string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";

            double size = bytes;
            string[] units = { "B", "KB", "MB", "GB" };
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:F1} {units[unitIndex]}";
        }

        private void FlashButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            SetSelectedPartitions();
            this.Hide();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Hide();
        }

        public void SetSelectedPartitions()
        {
            SelectedPartitions = Partitions?.Where(p => p.IsSelected).ToList() ?? new List<PartitionInfo>();
        }

        private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
        {
            if (Partitions != null)
            {
                foreach (var partition in Partitions)
                {
                    partition.PropertyChanged -= OnPartitionPropertyChanged;
                }
            }

            this.Closed -= OnDialogClosed;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
