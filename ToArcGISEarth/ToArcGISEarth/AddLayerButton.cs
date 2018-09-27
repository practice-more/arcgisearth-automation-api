﻿// Copyright 2018 Esri
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using ArcGIS.Core.CIM;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Timers;
using static ToArcGISEarth.ToolHelper;

namespace ToArcGISEarth
{
    public class AddLayerButton : Button
    {
        private const string MESSAGE_TIPS = "Failed to add layer to ArcGIS Earth.";

        #region  ElevationSource variable and property

        private Timer _timer;
        private event PropertyChangedEventHandler ElevationSourceAddedChanged;
        private event PropertyChangedEventHandler ElevationSourceRemovedChanged;
        private List<string[]> _elevationSources = new List<string[]>();
        //  private bool? addOrRemove = null; // True: added elevation source, false: removed elevation source, null: do nothing
        private ElevationSourcesOperation _sourcesOperation = ElevationSourcesOperation.None;
        private CIMMap _myIMMap = new CIMMap();
        public CIMMap MyCIMMap
        {
            get { return _myIMMap; }
            set
            {
                _elevationSources = ToolHelper.AddedOrRemovedElevationSources(_myIMMap.ElevationSurfaces, value?.ElevationSurfaces, ref _sourcesOperation);
                if (IsElevationSourceAddedChanged() && IsChecked)
                {
                    _myIMMap = value;
                    ElevationSourceAddedChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MyCIMMap)));
                }
                if (IsElevationSourceRemovedChanged() && RemoveLayerButton.HasChecked)
                {
                    _myIMMap = value;
                    ElevationSourceRemovedChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MyCIMMap)));
                }
            }
        }

        #endregion ElevationSource variable and property

        public AddLayerButton()
        {
            Enabled = false;
            _timer = new Timer
            {
                Enabled = true,
                Interval = 1000
            };
            // Update current MyCIMMap every 1 second
            _timer.Elapsed += (s, e) =>
            {
                QueuedTask.Run(() =>
                {
                    MyCIMMap = MapView.Active?.Map?.GetDefinition();
                });
            };
            ElevationSourceAddedChanged += ElevationSourceAddedEvent;
            ElevationSourceRemovedChanged += ElevationSourceRemovedEvent;
        }

        protected override void OnClick()
        {
            if (IsChecked)
            {
                LayersAddedEvent.Unsubscribe(AddLayer);
                _timer.Stop();
                IsChecked = false;
                if (RemoveLayerButton.HasChecked)
                {
                    ElevationSourceRemovedChanged += ElevationSourceRemovedEvent;
                }
            }
            else
            {
                if (RemoveLayerButton.HasChecked)
                {
                    ElevationSourceRemovedChanged += ElevationSourceRemovedEvent;
                }
                LayersAddedEvent.Subscribe(AddLayer, false);
                QueuedTask.Run(() =>
                {
                    _myIMMap = MapView.Active.Map.GetDefinition();
                });
                _timer.Enabled = true;
                _timer.Start();
                IsChecked = true;
            }
        }

        protected override void OnUpdate()
        {
            if (ToolHelper.IsConnectSuccessfully)
            {
                Enabled = true;
            }
            else
            {
                LayersAddedEvent.Unsubscribe(AddLayer);
                Enabled = false;
                IsChecked = false;
            }
        }

        private void AddLayer(LayerEventsArgs args)
        {
            try
            {
                List<Layer> layerList = args.Layers as List<Layer>;
                if (layerList?.Count != 0 && !IsCreateNewGroupLayer(layerList))
                {
                    foreach (var layer in layerList)
                    {
                        QueuedTask.Run(() =>
                        {
                            // This method or property must be called within the lambda passed to QueuedTask.Run. 
                            CIMObject dataConnection = layer.GetDataConnection();
                            string url = ToolHelper.GetDataSource(dataConnection);
                            if (!String.IsNullOrWhiteSpace(url))
                            {
                                JObject addLayerJson = new JObject
                                {
                                    ["URI"] = url
                                };
                                if (dataConnection is CIMWMSServiceConnection)
                                {
                                    addLayerJson["type"] = "OGCWMS"; // ArcGIS Earth Auotmation API can't autoatic recognize wms service.
                                }
                                if (layer.MapLayerType == ArcGIS.Core.CIM.MapLayerType.Operational)
                                {
                                    addLayerJson["target"] = "OperationalLayers";
                                }
                                if (layer.MapLayerType == ArcGIS.Core.CIM.MapLayerType.BasemapBackground)
                                {
                                    addLayerJson["target"] = "BasemapLayers";
                                }
                                string currentJson = addLayerJson.ToString();
                                string[] nameAndType = new string[2]
                                {
                                    layer.Name,
                                    layer.MapLayerType.ToString()
                                };
                                string id = ToolHelper.Utils.AddLayer(currentJson);
                                if (!ToolHelper.IdNameDic.Keys.Contains(id))
                                {
                                    ToolHelper.IdNameDic.Add(id, nameAndType);
                                }
                            }
                            else
                            {
                                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(MESSAGE_TIPS.Remove(MESSAGE_TIPS.Length - 1, 1) + " : " + layer.Name);
                            }
                        });
                    }
                }
            }
            catch
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(MESSAGE_TIPS);
            }
        }

        private bool IsCreateNewGroupLayer(List<Layer> layerList)
        {
            return layerList?.Count == 1 && layerList[0]?.Name == "New Group Layer" && (layerList[0].GetType()?.GetProperty("Layers")?.GetValue(layerList[0]) as List<Layer>) == null;
        }

        private bool IsElevationSourceAddedChanged()
        {
            if (_elevationSources != null && _elevationSources.Count > 0)
            {
                // Added elevation source
                if (_sourcesOperation == ElevationSourcesOperation.Add)
                {
                    return true;
                }
                return false;
            }
            return false;
        }

        private bool IsElevationSourceRemovedChanged()
        {
            if (_elevationSources != null && _elevationSources.Count > 0)
            {
                // Removed elevation source
                if (_sourcesOperation == ElevationSourcesOperation.Remove)
                {
                    return true;
                }
                return false;
            }
            return false;
        }

        private void ElevationSourceAddedEvent(object sender, PropertyChangedEventArgs args)
        {
            if (_sourcesOperation == ElevationSourcesOperation.Add && _elevationSources?.Count > 0)
            {
                string url = null;
                if (_elevationSources.FirstOrDefault()?.Length >= 3)
                {
                    url = _elevationSources.FirstOrDefault()[2];
                }
                JObject addLayerJson = new JObject
                {
                    ["URI"] = url,
                    ["target"] = "ElevationLayers"
                };
                string currentJson = addLayerJson.ToString();
                string[] nameAndType = new string[2]
                {
                         url,
                        "ElevationLayers"
                };
                try
                {
                    string id = ToolHelper.Utils.AddLayer(currentJson);
                    if (!ToolHelper.IdNameDic.Keys.Contains(id))
                    {
                        ToolHelper.IdNameDic.Add(id, nameAndType);
                    }
                }
                catch
                {
                    ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(MESSAGE_TIPS);
                }
            }
        }

        private void ElevationSourceRemovedEvent(object sender, PropertyChangedEventArgs args)
        {
            if (_sourcesOperation == ElevationSourcesOperation.Remove && _elevationSources?.Count > 0)
            {
                try
                {
                    List<string> idList = new List<string>();
                    foreach (var source in _elevationSources)
                    {
                        foreach (var item in ToolHelper.IdNameDic)
                        {
                            if (item.Value?.Length == 2 && source?.Length >= 3 && item.Value[0] == source[2] && item.Value[1] == "ElevationLayers")
                            {
                                idList.Add(item.Key);
                            }
                        }
                    }
                    foreach (var id in idList)
                    {
                        ToolHelper.Utils.RemoveLayer(id);
                        ToolHelper.IdNameDic.Remove(id);
                    }
                    return;
                }
                catch
                {
                }
            }
        }
    }
}
