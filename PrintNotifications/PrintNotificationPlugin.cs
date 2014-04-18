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
using MatterHackers.MatterControl.ActionBar;

namespace MatterHackers.MatterControl.Plugins.PrintNotifications
{
    public class PrintNotificationPlugin : MatterControlPlugin
    {
        public PrintNotificationPlugin()
        { 
        }

        GuiWidget mainApplication;
        event EventHandler unregisterEvents;
        public override void Initialize(GuiWidget application)
        {
            mainApplication = application;
            PrinterCommunication.Instance.PrintFinished.RegisterEvent(SendPrintFinishedNotification, ref unregisterEvents);
            PrintStatusRow.AddIconToPrintStatusRow += AddNotificationButton;
        }

        private static void AddNotificationButton(GuiWidget iconContainer)
        {
            ImageButtonFactory imageButtonFactory = new ImageButtonFactory();
            imageButtonFactory.invertImageColor = false;
            string notifyIconPath = Path.Combine("Icons", "PrintStatusControls", "notify.png");
            string notifyHoverIconPath = Path.Combine("Icons", "PrintStatusControls", "notify-hover.png");
            Button notifyButton = imageButtonFactory.Generate(notifyIconPath, notifyHoverIconPath);
            notifyButton.Cursor = Cursors.Hand;
            notifyButton.Margin = new Agg.BorderDouble(top: 3);
            notifyButton.Click += (sender, mouseEvent) => { NotificationFormWindow.Open(); };
            notifyButton.MouseEnterBounds += (sender, mouseEvent) => { HelpTextWidget.Instance.ShowHoverText("Edit notification settings"); };
            notifyButton.MouseLeaveBounds += (sender, mouseEvent) => { HelpTextWidget.Instance.HideHoverText(); };

            iconContainer.AddChild(notifyButton);
        }

        public override string GetPluginInfoJSon()
        {
            return "{" +
                "\"Name\": \"Print Notifications\"," +
                "\"UUID\": \"336afe80-66c4-11e3-949a-0800200c9a66\"," +
                "\"About\": \"A plugin that allows you to recieve a notification when your print completes by SMS or Email.\"," +
                "\"Developer\": \"MatterHackers, Inc.\"," +
                "\"URL\": \"https://www.matterhackers.com\"" +
                "}";
        }

        public void SendPrintFinishedNotification(object sender, EventArgs e)
        {
            PrintItemWrapperEventArgs printItemWrapperEventArgs = e as PrintItemWrapperEventArgs;
            if (printItemWrapperEventArgs != null)
            {
                if (UserSettings.Instance.get("AfterPrintFinishedPlaySound") == "true")
                {
                    try
                    {
                        string notificationSound = Path.Combine(ApplicationDataStorage.Instance.ApplicationStaticDataPath, "Sounds", "timer-done.wav");
                        (new SoundPlayer(notificationSound)).Play();
                    }
                    catch
                    {
                        UserSettings.Instance.set("AfterPrintFinishedPlaySound", "false");
                    }
                }

                if (UserSettings.Instance.get("AfterPrintFinishedSendEmail") == "true" || UserSettings.Instance.get("AfterPrintFinishedSendTextMessage") == "true")
                {
                    try
                    {
                        NotificationRequest notificationRequest = new NotificationRequest(printItemWrapperEventArgs.PrintItemWrapper.Name);
                        notificationRequest.Request();
                    }
                    catch
                    {
                        //
                    }
                }
            }
        }
    }
}
