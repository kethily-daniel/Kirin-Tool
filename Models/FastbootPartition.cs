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

using System.ComponentModel;
using System.IO;

namespace Kirin_Tool.Models
{
    public class FastbootPartition : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _dumpPath;

        public string Name { get; set; }
        public string Identifier { get; set; }
        public string ImageFileName => !string.IsNullOrEmpty(DumpPath) ? Path.GetFileName(DumpPath) : $"{Identifier}.img";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string DumpPath
        {
            get => _dumpPath;
            set
            {
                _dumpPath = value;
                OnPropertyChanged(nameof(DumpPath));
            }
        }

        public FastbootPartition(string name)
        {
            Name = name.Trim();
            Identifier = name.ToLowerInvariant().Trim();
            IsSelected = !Identifier.Equals("userdata", StringComparison.OrdinalIgnoreCase);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
