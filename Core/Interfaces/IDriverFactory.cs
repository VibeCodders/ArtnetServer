using System;
using ArtnetNode.Drivers;

namespace ArtnetNode.Core.Interfaces
{
    public interface IDriverFactory
    {
        IDmxInterface CreateDriver(string driverType, int universe);
    }
}
