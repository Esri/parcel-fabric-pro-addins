/*
   Copyright 2025 Esri

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       https://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

   See the License for the specific language governing permissions and
   limitations under the License.

*/
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.PluginDatastore;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Internal.Mapping.Locate;
using ArcGIS.Desktop.KnowledgeGraph;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
using ESRI.ArcGIS.ItemIndex;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace LandXMLProjectItem
{
  internal class LandXMLProjectItem : CustomProjectItemBase, IMappableItem
  {

    //Map must be 2D with a Graphics Layer added
    public bool CanAddToMap(MapType? mapType)
    {
      if (mapType != null)
      {
        if (mapType != MapType.Map)
          return false;
      }
      return true;
//      return MapView.Active?.Map.GetLayersAsFlattenedList()
//          .OfType<GraphicsLayer>().FirstOrDefault() != null;
    }

    public List<string> OnAddToMap(Map map)
    {
      var dataSourceFile = System.IO.Path.GetFileNameWithoutExtension(this.Path);
      
      OperationManager opManager = new();

      // Create a new group layer
      GroupLayer groupLayer =
        LayerFactory.Instance.CreateGroupLayer(map, 0, dataSourceFile);
      
      return OnAddToMap(map, groupLayer, -1);

    }
    
    public List<string> OnAddToMap(Map map, ILayerContainerEdit groupLayer, int index)
    {
      var pluginID = @"LandXMLPluginDataSource_Datasource";
      var dataSourceFolder = System.IO.Path.GetDirectoryName(this.Path);
      // Creating a URI from the path string
      Uri dataSourceFolderURI = new Uri(dataSourceFolder);
      Uri dataFilePathURI = new Uri(this.Path);
      //var dataSourceFile = System.IO.Path.GetFileNameWithoutExtension(this.Path);
      //Uri dataSourceFileURI = new Uri(dataSourceFolder);

      var conxLandXML = new PluginDatasourceConnectionPath(pluginID, dataSourceFolderURI);

      //var conxLandXML = new PluginDatasourceConnectionPath(pluginID, dataFilePathURI);
      PluginDatastore pluginLandXML = new(conxLandXML);

      var uri = Module1.AddLandXMLGroupLayer(groupLayer as GroupLayer, pluginLandXML);
      return new List<string> { this.Path };
    }

    protected LandXMLProjectItem() : base()
    {
    }

    protected LandXMLProjectItem(ItemInfoValue iiv) : base(FlipBrowseDialogOnly(iiv))
    {

    }

    private static ItemInfoValue FlipBrowseDialogOnly(ItemInfoValue iiv)
    {
      iiv.browseDialogOnly = "FALSE";
      return iiv;
    }

    //TODO: Overload for use in your container create item
    public LandXMLProjectItem(string name, string catalogPath, string typeID, string containerTypeID) :
      base(name, catalogPath, typeID, containerTypeID)
    {

    }

    public override ImageSource LargeImage
    {
      get
      {
        var largeImg = Application.Current.Resources["GeodatabaseCadasatralFabric32"] as ImageSource;
        return largeImg;
      }
    }

    public override Task<ImageSource> SmallImage
    {
      get
      {
        var smallImage = Application.Current.Resources["GeodatabaseCadastralFabric16"] as ImageSource;
        if (smallImage == null) throw new ArgumentException("SmallImage for CustomProjectItem doesn't exist");
        return Task.FromResult(smallImage as ImageSource);
      }
    }
    public override ProjectItemInfo OnGetInfo()
    {
      var projectItemInfo = new ProjectItemInfo
      {
        Name = this.Name,
        Path = this.Path,
        Type = LandXMLProjectItemContainer.ContainerName
      };

      return projectItemInfo;
    }

    public override bool IsContainer => false;

    //TODO: Fetch is required if <b>IsContainer</b> = <b>true</b>
    //public override void Fetch()
    //    {
    //TODO Retrieve your child items
    //TODO child items must also derive from CustomItemBase
    //this.AddRangeToChildren(children);
    //   }
  }
  internal class AddToProjectLandXMLProjectItem : Button
  {
    protected override void OnClick()
    {
      var catalog = Project.GetCatalogPane();
      var items = catalog.SelectedItems;
      var item = items.OfType<LandXMLProjectItem>().FirstOrDefault();
      if (item == null)
        return;
      QueuedTask.Run(() =>
      {
        foreach (var it in items.OfType<LandXMLProjectItem>())
          Project.Current.AddItem(it);
      });
    }
  }

  internal class RemoveFromProjectLandXMLProjectItem : Button
  {
    protected override void OnClick()
    {
      var catalog = Project.GetCatalogPane();
      var items = catalog.SelectedItems;
      var item = items.OfType<LandXMLProjectItem>().FirstOrDefault();
      if (item == null)
        return;
      QueuedTask.Run(() => Project.Current.RemoveItems(items.OfType<LandXMLProjectItem>()));
    }
  }

}
