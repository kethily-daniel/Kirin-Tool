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
using System.Collections.Generic;
using System.ComponentModel;

namespace Kirin_Tool.Models
{
    public class FullOtaFile : INotifyPropertyChanged
    {
        private string _filePath;
        private bool _isSelected;
        private bool _isEditEnabled = true;
        private List<PartitionInfo> _availablePartitions;
        private List<PartitionInfo> _selectedPartitions;

        public string FileType { get; set; }
        public string DisplayName { get; set; }

        public bool IsEditEnabled
        {
            get => _isEditEnabled;
            set
            {
                _isEditEnabled = value;
                OnPropertyChanged(nameof(IsEditEnabled));
            }
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                OnPropertyChanged(nameof(FilePath));
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(HasFile));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public List<PartitionInfo> AvailablePartitions
        {
            get => _availablePartitions ?? new List<PartitionInfo>();
            set
            {
                _availablePartitions = value;
                OnPropertyChanged(nameof(AvailablePartitions));
            }
        }

        public List<PartitionInfo> SelectedPartitions
        {
            get => _selectedPartitions ?? new List<PartitionInfo>();
            set
            {
                _selectedPartitions = value;
                OnPropertyChanged(nameof(SelectedPartitions));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string FileName => !string.IsNullOrEmpty(FilePath) ? System.IO.Path.GetFileName(FilePath) : "No file selected";
        public bool HasFile => !string.IsNullOrEmpty(FilePath);
        public string StatusText
        {
            get
            {
                if (!HasFile) return "No file selected";
                var selectedCount = SelectedPartitions?.Count ?? AvailablePartitions.Count;
                var totalCount = AvailablePartitions.Count;
                return $"{selectedCount}/{totalCount} partitions selected";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
