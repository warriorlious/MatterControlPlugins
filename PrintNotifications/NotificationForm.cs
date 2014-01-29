using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.UI;
using MatterHackers.Agg.OpenGlGui;
using MatterHackers.PolygonMesh;
using MatterHackers.RenderOpenGl;
using MatterHackers.VectorMath;
using MatterHackers.MatterControl.DataStorage;
using MatterHackers.MatterControl.PrintQueue;
using MatterHackers.MatterControl.VersionManagement;
using MatterHackers.MatterControl.FieldValidation;

namespace MatterHackers.MatterControl.Plugins.PrintNotifications
{
    public class FormField
    {
        public delegate ValidationStatus ValidationHandler(string valueToValidate);
        public MHTextEditWidget FieldEditWidget { get; set; }
        public TextWidget FieldErrorMessageWidget { get; set; }
        ValidationHandler[] FieldValidationHandlers { get; set; }

        public FormField(MHTextEditWidget textEditWidget, TextWidget errorMessageWidget, ValidationHandler[] validationHandlers)
        {
            this.FieldEditWidget = textEditWidget;
            this.FieldErrorMessageWidget = errorMessageWidget;
            this.FieldValidationHandlers = validationHandlers;
        }

        public bool Validate()
        {
            bool fieldIsValid = true;
            foreach (ValidationHandler validationHandler in FieldValidationHandlers)
            {
                if (fieldIsValid)
                {
                    ValidationStatus validationStatus = validationHandler(this.FieldEditWidget.Text);
                    if (!validationStatus.IsValid)
                    {
                        fieldIsValid = false;
                        FieldErrorMessageWidget.Text = validationStatus.ErrorMessage;
                        FieldErrorMessageWidget.Visible = true;
                    }
                }
            }
            return fieldIsValid;
        }
    }
    
    public class NotificationFormWidget : GuiWidget
    {
        protected TextImageButtonFactory textImageButtonFactory = new TextImageButtonFactory();
        protected TextImageButtonFactory whiteButtonFactory = new TextImageButtonFactory();
        Button saveButton;
        Button cancelButton;
        Button doneButton;
        FlowLayoutWidget formContainer;
        FlowLayoutWidget messageContainer;
        CheckBox notifySendTextCheckbox;
        CheckBox notifyPlaySoundCheckbox;
        CheckBox notifySendEmailCheckbox;
        GuiWidget phoneNumberLabel;
        GuiWidget phoneNumberHelperLabel;

        FlowLayoutWidget phoneNumberContainer;
        FlowLayoutWidget emailAddressContainer;

        GuiWidget emailAddressLabel;
        GuiWidget emailAddressHelperLabel;
        MHTextEditWidget emailAddressInput;
        TextWidget emailAddressErrorMessage;

        TextWidget submissionStatus;
        GuiWidget centerContainer;

        MHTextEditWidget phoneNumberInput;
        TextWidget phoneNumberErrorMessage;

        public NotificationFormWidget()
        {
            SetButtonAttributes();
            AnchorAll();

            cancelButton = textImageButtonFactory.Generate("Cancel");
            saveButton = textImageButtonFactory.Generate("Save");
            doneButton = textImageButtonFactory.Generate("Done");
            doneButton.Visible = false;

            DoLayout();
            AddButtonHandlers();
        }

        private GuiWidget LabelGenerator(string labelText, int fontSize = 12, int height = 28)
        {
            GuiWidget labelContainer = new GuiWidget();
            labelContainer.HAnchor = HAnchor.ParentLeftRight;
            labelContainer.Height = height;

            TextWidget formLabel = new TextWidget(labelText, pointSize: fontSize);
            formLabel.TextColor = ActiveTheme.Instance.PrimaryTextColor;
            formLabel.VAnchor = VAnchor.ParentBottom;
            formLabel.HAnchor = HAnchor.ParentLeft;
            formLabel.Margin = new BorderDouble(bottom: 2);

            labelContainer.AddChild(formLabel);

            return labelContainer;
        }

        private TextWidget ErrorMessageGenerator()
        {
            TextWidget formLabel = new TextWidget("", pointSize:11);
            formLabel.AutoExpandBoundsToText = true;
            formLabel.Margin = new BorderDouble(0, 5);
            formLabel.TextColor = RGBA_Bytes.Red;            
            formLabel.HAnchor = HAnchor.ParentLeft;
            formLabel.Visible = false;            

            return formLabel;
        }

        private void DoLayout()
        {
            FlowLayoutWidget mainContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
            mainContainer.AnchorAll();

            FlowLayoutWidget labelContainer = new FlowLayoutWidget();
            labelContainer.HAnchor = HAnchor.ParentLeftRight;            

            TextWidget formLabel = new TextWidget("After a Print is Finished:", pointSize:16);
            formLabel.TextColor = ActiveTheme.Instance.PrimaryTextColor;
            formLabel.VAnchor = VAnchor.ParentCenter;
            formLabel.Margin = new BorderDouble(10, 0,10, 12);
            labelContainer.AddChild(formLabel);
            mainContainer.AddChild(labelContainer);

            centerContainer = new GuiWidget();
            centerContainer.AnchorAll();
            centerContainer.Padding = new BorderDouble(10);

            messageContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
            messageContainer.AnchorAll();
            messageContainer.Visible = false;
            messageContainer.BackgroundColor = ActiveTheme.Instance.SecondaryBackgroundColor;
            messageContainer.Padding = new BorderDouble(10);
            
            submissionStatus = new TextWidget("Saving your settings...", pointSize: 13);
            submissionStatus.AutoExpandBoundsToText = true;
            submissionStatus.Margin = new BorderDouble(0, 5);
            submissionStatus.TextColor = RGBA_Bytes.White;
            submissionStatus.HAnchor = HAnchor.ParentLeft;

            messageContainer.AddChild(submissionStatus);

            formContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
            formContainer.AnchorAll();
            formContainer.BackgroundColor = ActiveTheme.Instance.SecondaryBackgroundColor;
            formContainer.Padding = new BorderDouble(10);
            {
                FlowLayoutWidget smsLabelContainer = new FlowLayoutWidget(FlowDirection.LeftToRight);
                smsLabelContainer.Margin = new BorderDouble(0, 2, 0, 4);
                smsLabelContainer.HAnchor |= Agg.UI.HAnchor.ParentLeft;
                
                //Add sms notification option
                notifySendTextCheckbox = new CheckBox("Send an SMS notification");
                notifySendTextCheckbox.Margin = new BorderDouble(0);
                notifySendTextCheckbox.VAnchor = Agg.UI.VAnchor.ParentBottom;
                notifySendTextCheckbox.TextColor = RGBA_Bytes.White;
                notifySendTextCheckbox.Cursor = Cursors.Hand;
                notifySendTextCheckbox.Checked = (UserSettings.Instance.get("AfterPrintFinishedSendTextMessage") == "true");
                notifySendTextCheckbox.CheckedStateChanged += new CheckBox.CheckedStateChangedEventHandler(OnSendTextChanged);

                TextWidget experimentalLabel = new TextWidget("Experimental", pointSize:10);
                experimentalLabel.TextColor = ActiveTheme.Instance.SecondaryAccentColor;
                experimentalLabel.VAnchor = Agg.UI.VAnchor.ParentBottom;
                experimentalLabel.Margin = new BorderDouble(left:10);

                smsLabelContainer.AddChild(notifySendTextCheckbox);
                smsLabelContainer.AddChild(experimentalLabel);

                formContainer.AddChild(smsLabelContainer);
                formContainer.AddChild(LabelGenerator("Have MatterControl send you a text message after your print is finished", 9, 14));

                phoneNumberContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
                phoneNumberContainer.HAnchor = Agg.UI.HAnchor.ParentLeftRight;

                phoneNumberLabel = LabelGenerator("Your Phone Number*");
                phoneNumberHelperLabel = LabelGenerator("A U.S. or Canadian mobile phone number", 9, 14);
                

                phoneNumberContainer.AddChild(phoneNumberLabel);
                phoneNumberContainer.AddChild(phoneNumberHelperLabel);

                phoneNumberInput = new MHTextEditWidget();
                phoneNumberInput.HAnchor = HAnchor.ParentLeftRight;

                string phoneNumber = UserSettings.Instance.get("NotificationPhoneNumber");
                if (phoneNumber != null)
                {
                    phoneNumberInput.Text = phoneNumber;
                }

                phoneNumberContainer.AddChild(phoneNumberInput);

                phoneNumberErrorMessage = ErrorMessageGenerator();
                phoneNumberContainer.AddChild(phoneNumberErrorMessage);

                formContainer.AddChild(phoneNumberContainer);
            }

            {
                //Add email notification option
                notifySendEmailCheckbox = new CheckBox("Send an email notification");
                notifySendEmailCheckbox.Margin = new BorderDouble(0, 2, 0, 16);
                notifySendEmailCheckbox.HAnchor = Agg.UI.HAnchor.ParentLeft;
                notifySendEmailCheckbox.TextColor = RGBA_Bytes.White;
                notifySendEmailCheckbox.Cursor = Cursors.Hand;
                notifySendEmailCheckbox.Checked = (UserSettings.Instance.get("AfterPrintFinishedSendEmail") == "true");
                notifySendEmailCheckbox.CheckedStateChanged += new CheckBox.CheckedStateChangedEventHandler(OnSendEmailChanged);

                formContainer.AddChild(notifySendEmailCheckbox);
                formContainer.AddChild(LabelGenerator("Have MatterControl send you an email message after your print is finished", 9, 14));

                emailAddressContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
                emailAddressContainer.HAnchor = Agg.UI.HAnchor.ParentLeftRight;

                emailAddressLabel = LabelGenerator("Your Email Address*");

                emailAddressHelperLabel = LabelGenerator("A valid email address", 9, 14);

                emailAddressContainer.AddChild(emailAddressLabel);
                emailAddressContainer.AddChild(emailAddressHelperLabel);

                emailAddressInput = new MHTextEditWidget();
                emailAddressInput.HAnchor = HAnchor.ParentLeftRight;

                string emailAddress = UserSettings.Instance.get("NotificationEmailAddress");
                if (emailAddress != null)
                {
                    emailAddressInput.Text = emailAddress;
                }

                emailAddressContainer.AddChild(emailAddressInput);

                emailAddressErrorMessage = ErrorMessageGenerator();
                emailAddressContainer.AddChild(emailAddressErrorMessage);

                formContainer.AddChild(emailAddressContainer);
            }

            notifyPlaySoundCheckbox = new CheckBox("Play a Sound");
            notifyPlaySoundCheckbox.Margin = new BorderDouble(0, 2, 0, 16);
            notifyPlaySoundCheckbox.HAnchor = Agg.UI.HAnchor.ParentLeft;
            notifyPlaySoundCheckbox.TextColor = RGBA_Bytes.White;
            notifyPlaySoundCheckbox.Checked = (UserSettings.Instance.get("AfterPrintFinishedPlaySound") == "true");
            notifyPlaySoundCheckbox.Cursor = Cursors.Hand;

            formContainer.AddChild(notifyPlaySoundCheckbox);
            formContainer.AddChild(LabelGenerator("Play a sound after your print is finished", 9, 14));                            

            centerContainer.AddChild(formContainer);

            mainContainer.AddChild(centerContainer);
            
            FlowLayoutWidget buttonBottomPanel = GetButtonButtonPanel();
            buttonBottomPanel.AddChild(saveButton);
            buttonBottomPanel.AddChild(cancelButton);
            buttonBottomPanel.AddChild(doneButton);

            mainContainer.AddChild(buttonBottomPanel);

            this.AddChild(mainContainer);

            SetVisibleStates();
        }

        void OnSendTextChanged(object sender, EventArgs e)
        {
            SetVisibleStates();
        }

        void OnSendEmailChanged(object sender, EventArgs e)
        {
            SetVisibleStates();
        }

        void SetVisibleStates()
        {
            phoneNumberContainer.Visible =  notifySendTextCheckbox.Checked;
            emailAddressContainer.Visible = notifySendEmailCheckbox.Checked;
        }

        private bool ValidateContactForm()
        {
            ValidationMethods validationMethods = new ValidationMethods();
            
            List<FormField> formFields = new List<FormField>{};
            FormField.ValidationHandler[] stringValidationHandlers = new FormField.ValidationHandler[] { validationMethods.StringIsNotEmpty };
            FormField.ValidationHandler[] emailValidationHandlers = new FormField.ValidationHandler[] { validationMethods.StringIsNotEmpty, validationMethods.StringLooksLikeEmail };
            FormField.ValidationHandler[] phoneValidationHandlers = new FormField.ValidationHandler[] { validationMethods.StringIsNotEmpty, validationMethods.StringLooksLikePhoneNumber };

            formFields.Add(new FormField(phoneNumberInput, phoneNumberErrorMessage, phoneValidationHandlers));
            formFields.Add(new FormField(emailAddressInput, emailAddressErrorMessage, emailValidationHandlers));

            bool formIsValid = true;
            foreach (FormField formField in formFields)
            {
                //Only validate field if visible
                if (formField.FieldEditWidget.Parent.Visible == true)
                {
                    formField.FieldErrorMessageWidget.Visible = false;
                    bool fieldIsValid = formField.Validate();
                    if (!fieldIsValid)
                    {
                        formIsValid = false;
                    }
                }
            }
            return formIsValid;
        }

        private void AddButtonHandlers()
        {
            cancelButton.Click += (sender, e) => { Close(); };
            doneButton.Click += (sender, e) => { Close(); };
            saveButton.Click += new ButtonBase.ButtonEventHandler(SubmitContactForm);
        }

        void SubmitContactForm(object sender, MouseEventArgs mouseEvent)
        {
            if (ValidateContactForm())
            {
                if (notifySendTextCheckbox.Checked)
                {
                    UserSettings.Instance.set("AfterPrintFinishedSendTextMessage", "true");
                    UserSettings.Instance.set("NotificationPhoneNumber", phoneNumberInput.Text);
                }
                else
                {
                    UserSettings.Instance.set("AfterPrintFinishedSendTextMessage", "false");
                }

                if (notifySendEmailCheckbox.Checked)
                {
                    UserSettings.Instance.set("AfterPrintFinishedSendEmail", "true");
                    UserSettings.Instance.set("NotificationEmailAddress", emailAddressInput.Text);
                }
                else
                {
                    UserSettings.Instance.set("AfterPrintFinishedSendEmail", "false");
                }

                if (notifyPlaySoundCheckbox.Checked)
                {
                    UserSettings.Instance.set("AfterPrintFinishedPlaySound", "true");
                }
                else
                {
                    UserSettings.Instance.set("AfterPrintFinishedPlaySound", "false");
                }

                if (ApplicationSettings.Instance.get("ClientToken") == null)
                {
                    RequestClientToken request = new RequestClientToken();
                    //request.RequestSucceeded += new EventHandler(onClientTokenRequestSucceeded);
                    request.Request();
                }

                Close();              
            }
        }

        private FlowLayoutWidget GetButtonButtonPanel()
        {
            FlowLayoutWidget buttonBottomPanel = new FlowLayoutWidget(FlowDirection.LeftToRight);
            buttonBottomPanel.HAnchor = HAnchor.ParentLeftRight;
            buttonBottomPanel.Padding = new BorderDouble(10, 3);
            buttonBottomPanel.BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor;
            return buttonBottomPanel;
        }

        private void SetButtonAttributes()
        {
            textImageButtonFactory.normalTextColor = RGBA_Bytes.White;
            textImageButtonFactory.hoverTextColor = RGBA_Bytes.White;
            textImageButtonFactory.disabledTextColor = RGBA_Bytes.White;
            textImageButtonFactory.pressedTextColor = RGBA_Bytes.White;

            whiteButtonFactory.FixedWidth = 138;
            whiteButtonFactory.normalFillColor = RGBA_Bytes.White;
            whiteButtonFactory.normalTextColor = RGBA_Bytes.Black;
            whiteButtonFactory.hoverTextColor = RGBA_Bytes.Black;
            whiteButtonFactory.hoverFillColor = new RGBA_Bytes(255, 255, 255, 200);
        }
    }

    public class NotificationFormWindow : SystemWindow
    {
        static NotificationFormWindow contactFormWindow;
        static bool contactFormIsOpen;

        static public void Open()
        {
            if (!contactFormIsOpen)
            {
                contactFormWindow = new NotificationFormWindow();
                contactFormIsOpen = true;
                contactFormWindow.Closed += (sender, e) => { contactFormIsOpen = false; };                
            }
            else
            {
                if (contactFormWindow != null)
                {
                    contactFormWindow.BringToFront();
                }
            }
        }

        NotificationFormWidget contactFormWidget;

        private NotificationFormWindow()
            : base(500, 550)
        {
            Title = "MatterControl: Notification Options";

            BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor;

            contactFormWidget = new NotificationFormWidget();

            AddChild(contactFormWidget);
            AddHandlers();

            ShowAsSystemWindow();
            MinimumSize = new Vector2(500, 550);
        }

        event EventHandler unregisterEvents;
        private void AddHandlers()
        {
            ActiveTheme.Instance.ThemeChanged.RegisterEvent(Instance_ThemeChanged, ref unregisterEvents);
            contactFormWidget.Closed += (sender, e) => { Close(); };
        }

        public override void OnClosed(EventArgs e)
        {
            if (unregisterEvents != null)
            {
                unregisterEvents(this, null);
            }
            base.OnClosed(e);
        }

        void Instance_ThemeChanged(object sender, EventArgs e)
        {
            Invalidate();
        }
    }
}

