﻿using ArcGIS.Core.CIM;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ToArcGISEarth
{
    public class AddLayerButton : Button
    {
        private const string MESSAGE_TIPS = "Failed to add layer to ArcGIS Earth.";

        public AddLayerButton()
        {
            this.Enabled = false;
        }

        protected override void OnClick()
        {
            if (this.IsChecked)
            {
                LayersAddedEvent.Unsubscribe(this.AddLayer);
                this.IsChecked = false;

            }
            else
            {
                LayersAddedEvent.Subscribe(this.AddLayer, false);
                this.IsChecked = true;
            }
        }

        protected override void OnUpdate()
        {
            if (ToolHelper.IsConnectSuccessfully)
            {
                this.Enabled = true;
            }
            else
            {
                LayersAddedEvent.Unsubscribe(this.AddLayer);
                this.Enabled = false;
                this.IsChecked = false;
            }
        }

        private void AddLayer(LayerEventsArgs args)
        {
            try
            {
                List<Layer> layerList = args.Layers as List<Layer>;
                if (layerList?.Count != 0 && !this.IsCreateNewGroupLayer(layerList))
                {
                    foreach (var layer in layerList)
                    {
                        QueuedTask.Run(() =>
                        {
                            // This method or property must be called within the lambda passed to QueuedTask.Run.
                            CIMObject dataConnection = layer.GetDataConnection();
                            string url = this.GetDataSource(dataConnection);
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

        private string GetDataSource(CIMObject dataConnection)
        {
            string source = null;
            if (dataConnection != null)
            {
                //  Shapfile, raster, tpk, Feature Layer
                if (dataConnection is CIMStandardDataConnection)
                {
                    WorkspaceFactory factory = (dataConnection as CIMStandardDataConnection).WorkspaceFactory;
                    string connectStr;
                    if (factory == WorkspaceFactory.FeatureService)
                    {
                        connectStr = (dataConnection as CIMStandardDataConnection).WorkspaceConnectionString; // e.g.  "URL=http://sampleserver3.arcgisonline.com/ArcGIS/rest/services/SanFrancisco/311Incidents/FeatureServer/0"
                        if (connectStr?.Length > 4)
                        {
                            return source = connectStr.Substring(4);
                        }
                    }
                    if (factory == WorkspaceFactory.Shapefile || factory == WorkspaceFactory.Raster)
                    {
                        string fileDirectory = "";
                        string fileName = (dataConnection as CIMStandardDataConnection).Dataset; // e.g.  "test.shp"
                        connectStr = (dataConnection as CIMStandardDataConnection).WorkspaceConnectionString; // e.g.  "DATABASE=D:\Temp"
                        if (connectStr?.Length > 9)
                        {
                            fileDirectory = connectStr.Substring(9) + Path.DirectorySeparatorChar;
                        }
                        return source = fileDirectory + fileName;
                    }
                }
                // Kml, kmz
                if (dataConnection is CIMKMLDataConnection)
                {
                    return source = (dataConnection as CIMKMLDataConnection).KMLURI;
                }
                // Spk, slpk
                if (dataConnection is CIMSceneDataConnection)
                {
                    string realUrl = (dataConnection as CIMSceneDataConnection).URI; // e.g. "file:/D:/temp/test.slpk/layers/0"
                    Uri.TryCreate(realUrl, UriKind.RelativeOrAbsolute, out Uri uri);
                    if (uri != null)
                    {
                        realUrl = uri.AbsolutePath;
                        if (realUrl.Length >= 9)
                        {
                            return source = realUrl.Remove(realUrl.Length - 9, 9); // e.g.  "/D:/temp/test.slpk"
                        }
                    }
                }
                // Imagary Layer, Map Image Layer, Tile Layer , Scene Layer
                if (dataConnection is CIMAGSServiceConnection)
                {
                    // Imager server
                    if ((dataConnection as CIMAGSServiceConnection).ObjectType == "ImageServer")
                    {
                        string url = (dataConnection as CIMAGSServiceConnection).URL;
                        if (url.Contains("services"))
                        {
                            string[] splitStr = url.Split(new string[] { "services" }, StringSplitOptions.None);
                            if (splitStr?.Length >= 2 && splitStr.FirstOrDefault() != null)
                            {
                                return splitStr[0] + "rest/" + "services" + splitStr[1];
                            }
                        }
                        return null;
                    }
                    return source = (dataConnection as CIMAGSServiceConnection).URL;
                }
                // Wms
                if (dataConnection is CIMWMSServiceConnection)
                {
                    return source = ((dataConnection as CIMWMSServiceConnection).ServerConnection as CIMProjectServerConnection).URL;
                }
            }
            return source;
        }

        private void aa(Layer layer)
        {
            string a = layer.GetDefinition().SourceURI;
        }
    }
}
