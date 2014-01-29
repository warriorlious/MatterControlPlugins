/*
Copyright (c) 2013, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Media;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using MatterHackers.Agg.UI;
using MatterHackers.MatterControl.PluginSystem;
using MatterHackers.MatterControl.DataStorage;
using MatterHackers.MatterControl;
using MatterHackers.MatterControl.VersionManagement;
using MatterHackers.MatterControl.PrintLibrary;

namespace MatterHackers.MatterControl.Plugins.OutlineCreator
{
    public class OutlineCreatorPlugin : MatterControlPlugin
    {
        public OutlineCreatorPlugin()
        { 
        }

        GuiWidget mainApplication;
        public override void Initialize(GuiWidget application)
        {
#if DEBUG
            CreatorInformation information = new CreatorInformation(LaunchNewOutlineCreator, "140.png", "Outline Creator");
            RegisteredCreators.Instance.RegisterLaunchFunction(information);
            mainApplication = application;
#endif
        }

        public override string GetPluginInfoJSon()
        {
            return "{" +
                "\"Name\": \"Outline Creator\"," +
                "\"UUID\": \"47768deb-d24b-4e8b-a8c5-7df8d7e2dcb2\"," +
                "\"About\": \"A Creator that allows you to load images and have them turned into printable extrusions.\","+
                "\"Developer\": \"MatterHackers, Inc.\"," +
                "\"URL\": \"https://www.matterhackers.com\"" +
                "}";
        }

        public void LaunchNewOutlineCreator(object sender, EventArgs e)
        {
            OutlineCreatorMainWindow mainWindow = new OutlineCreatorMainWindow();
        }
    }
}
