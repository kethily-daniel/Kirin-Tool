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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Wpf.Ui.Controls;
using System.Collections.Generic;

namespace Kirin_Tool.UI
{
    public partial class FastbootPartitionSelector : ContentDialog, INotifyPropertyChanged
    {
        private ObservableCollection<FastbootPartition> _partitions;
        private bool _updatingSelectAll = false;
        private bool? _selectAllState = false;
        private bool _skipSecurePartitions = true;

        public bool SkipSecurePartitions
        {
            get => _skipSecurePartitions;
            set
            {
                if (_skipSecurePartitions != value)
                {
                    _skipSecurePartitions = value;
                    OnPropertyChanged(nameof(SkipSecurePartitions));
                }
            }
        }

        public ObservableCollection<FastbootPartition> Partitions
        {
            get => _partitions;
            set
            {
                _partitions = value;
                OnPropertyChanged(nameof(Partitions));
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

        public string StatusText
        {
            get
            {
                if (Partitions == null) return "No partitions available";

                var selectedCount = Partitions.Count(p => p.IsSelected);
                var totalCount = Partitions.Count;
                return $"{selectedCount}/{totalCount} partitions selected";
            }
        }

        public List<FastbootPartition> SelectedPartitions { get; private set; }
        public bool DialogResult { get; private set; }

        public FastbootPartitionSelector(List<FastbootPartition> partitions)
        {
            InitializeComponent();

            Partitions = new ObservableCollection<FastbootPartition>(partitions ?? new List<FastbootPartition>());

            foreach (var partition in Partitions)
            {
                partition.PropertyChanged += OnPartitionPropertyChanged;
            }

            DataContext = this;
            UpdateSelectAllState();

            this.Closed += OnDialogClosed;
        }

        private void OnPartitionPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FastbootPartition.IsSelected))
            {
                if (!_updatingSelectAll)
                {
                    OnPropertyChanged(nameof(HasSelectedPartitions));
                    OnPropertyChanged(nameof(StatusText));
                    UpdateSelectAllState();
                }
            }
        }

        private void UpdateSelectAllState()
        {
            _updatingSelectAll = true;
            try
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
            finally
            {
                _updatingSelectAll = false;
            }
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
            OnPropertyChanged(nameof(StatusText));
        }

        

        public void SetSelectedPartitions()
        {
            SelectedPartitions = Partitions?.Where(p => p.IsSelected).ToList() ?? new List<FastbootPartition>();
        }

        private void ProceedButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            SetSelectedPartitions();


            try
            {
                this.Hide();
            }
            catch (Exception ex)
            {
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;

            try
            {
                this.Hide();
            }
            catch (Exception ex)
            {
            }
        }

        private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
        {

            if (args.Result == ContentDialogResult.Primary && !DialogResult)
            {
                DialogResult = true;
                SetSelectedPartitions();
            }

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
