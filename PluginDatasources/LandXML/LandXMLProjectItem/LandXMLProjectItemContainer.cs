using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
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

namespace LandXMLProjectItem
{
  internal class LandXMLProjectItemContainer : CustomProjectItemContainer<LandXMLProjectItem>
  {
    //This should be an arbitrary unique string. It must match your <content type="..." 
    //in the Config.daml for the container
    public static readonly string ContainerName = "LandXMLProjectItemContainer";
    public LandXMLProjectItemContainer() : base(ContainerName)
    {

    }

    /// <summary>
    /// Create item is called whenever a custom item, registered with the container,
    /// is browsed or fetched (eg the user is navigating through different folders viewing
    /// content in the catalog pane).
    /// </summary>
    /// <param name="name"></param>
    /// <param name="path"></param>
    /// <param name="containerType"></param>
    /// <param name="data"></param>
    /// <returns>A custom item created from the input parameters</returns>
    public override Item CreateItem(string name, string path, string containerType, string data)
    {
      var item = ItemFactory.Instance.Create(path);


      if (item is LandXMLProjectItemContainer)
      {
        this.Add(item as LandXMLProjectItem);

      }
      return item;
    }

    //public override Item DeleteItem(string name, string path, string containerType, string data)
    //{
    //  var item = ItemFactory.Instance.Create(path);


    //  if (item is LandXMLProjectItemContainer)
    //  {
    //    this.Remove(item as LandXMLProjectItem);

    //  }
    //  return item;
    //}

    public override ImageSource LargeImage
    {
      get
      {//Parent folder for land xml
        var largeImg = Application.Current.Resources["GeodatabaseCadastralFabric32"] as ImageSource;
        return largeImg;
      }
    }

    public override Task<System.Windows.Media.ImageSource> SmallImage
    {
      get
      {//Parent folder for land xml
        var smallImage = Application.Current.Resources["GeodatabaseCadastralFabric16"] as ImageSource;
        if (smallImage == null) throw new ArgumentException("SmallImage for CustomProjectContainer doesn't exist");
        return Task.FromResult(smallImage as ImageSource);
      }
    }

  }
}
