﻿using System;
using System.Collections.Generic;
using System.Linq;
using Acr.UserDialogs;
using Bit.App.Abstractions;
using Bit.App.Controls;
using Bit.App.Resources;
using Plugin.Connectivity.Abstractions;
using Xamarin.Forms;
using XLabs.Ioc;
using Bit.App.Utilities;

namespace Bit.App.Pages
{
    public class VaultEditCipherPage : ExtendedContentPage
    {
        private readonly string _cipherId;
        private readonly ICipherService _cipherService;
        private readonly IFolderService _folderService;
        private readonly IUserDialogs _userDialogs;
        private readonly IConnectivity _connectivity;
        private readonly IDeviceInfoService _deviceInfo;
        private readonly IGoogleAnalyticsService _googleAnalyticsService;
        private DateTime? _lastAction;

        public VaultEditCipherPage(string cipherId)
        {
            _cipherId = cipherId;
            _cipherService = Resolver.Resolve<ICipherService>();
            _folderService = Resolver.Resolve<IFolderService>();
            _userDialogs = Resolver.Resolve<IUserDialogs>();
            _connectivity = Resolver.Resolve<IConnectivity>();
            _deviceInfo = Resolver.Resolve<IDeviceInfoService>();
            _googleAnalyticsService = Resolver.Resolve<IGoogleAnalyticsService>();

            Init();
        }

        public FormEntryCell PasswordCell { get; private set; }
        public FormEntryCell UsernameCell { get; private set; }
        public FormEntryCell UriCell { get; private set; }
        public FormEntryCell NameCell { get; private set; }
        public FormEntryCell TotpCell { get; private set; }
        public FormEditorCell NotesCell { get; private set; }
        public FormPickerCell FolderCell { get; private set; }
        public ExtendedTextCell GenerateCell { get; private set; }
        public ExtendedTextCell AttachmentsCell { get; private set; }
        public ExtendedTextCell CustomFieldsCell { get; private set; }
        public ExtendedTextCell DeleteCell { get; private set; }

        private void Init()
        {
            var cipher = _cipherService.GetByIdAsync(_cipherId).GetAwaiter().GetResult();
            if(cipher == null)
            {
                // TODO: handle error. navigate back? should never happen...
                return;
            }

            NotesCell = new FormEditorCell(height: 300);
            NotesCell.Editor.Keyboard = Keyboard.Text;
            NotesCell.Editor.Text = cipher.Notes?.Decrypt(cipher.OrganizationId);

            TotpCell = new FormEntryCell(AppResources.AuthenticatorKey, nextElement: NotesCell.Editor,
                useButton: _deviceInfo.HasCamera);
            if(_deviceInfo.HasCamera)
            {
                TotpCell.Button.Image = "camera.png";
            }
            TotpCell.Entry.Text = cipher.Login?.Totp?.Decrypt(cipher.OrganizationId);
            TotpCell.Entry.DisableAutocapitalize = true;
            TotpCell.Entry.Autocorrect = false;
            TotpCell.Entry.FontFamily = Helpers.OnPlatform(iOS: "Menlo-Regular", Android: "monospace", WinPhone: "Courier");

            PasswordCell = new FormEntryCell(AppResources.Password, isPassword: true, nextElement: TotpCell.Entry,
                useButton: true);
            PasswordCell.Entry.Text = cipher.Login?.Password?.Decrypt(cipher.OrganizationId);
            PasswordCell.Button.Image = "eye.png";
            PasswordCell.Entry.DisableAutocapitalize = true;
            PasswordCell.Entry.Autocorrect = false;
            PasswordCell.Entry.FontFamily = Helpers.OnPlatform(iOS: "Menlo-Regular", Android: "monospace", WinPhone: "Courier");

            UsernameCell = new FormEntryCell(AppResources.Username, nextElement: PasswordCell.Entry);
            UsernameCell.Entry.Text = cipher.Login?.Username?.Decrypt(cipher.OrganizationId);
            UsernameCell.Entry.DisableAutocapitalize = true;
            UsernameCell.Entry.Autocorrect = false;

            UriCell = new FormEntryCell(AppResources.URI, Keyboard.Url, nextElement: UsernameCell.Entry);
            UriCell.Entry.Text = cipher.Login?.Uri?.Decrypt(cipher.OrganizationId);
            NameCell = new FormEntryCell(AppResources.Name, nextElement: UriCell.Entry);
            NameCell.Entry.Text = cipher.Name?.Decrypt(cipher.OrganizationId);

            GenerateCell = new ExtendedTextCell
            {
                Text = AppResources.GeneratePassword,
                ShowDisclousure = true
            };

            var folderOptions = new List<string> { AppResources.FolderNone };
            var folders = _folderService.GetAllAsync().GetAwaiter().GetResult()
                .OrderBy(f => f.Name?.Decrypt()).ToList();
            int selectedIndex = 0;
            int i = 0;
            foreach(var folder in folders)
            {
                i++;
                if(folder.Id == cipher.FolderId)
                {
                    selectedIndex = i;
                }

                folderOptions.Add(folder.Name.Decrypt());
            }
            FolderCell = new FormPickerCell(AppResources.Folder, folderOptions.ToArray());
            FolderCell.Picker.SelectedIndex = selectedIndex;

            var favoriteCell = new ExtendedSwitchCell
            {
                Text = AppResources.Favorite,
                On = cipher.Favorite
            };

            AttachmentsCell = new ExtendedTextCell
            {
                Text = AppResources.Attachments,
                ShowDisclousure = true
            };

            CustomFieldsCell = new ExtendedTextCell
            {
                Text = AppResources.CustomFields,
                ShowDisclousure = true
            };

            DeleteCell = new ExtendedTextCell { Text = AppResources.Delete, TextColor = Color.Red };

            var table = new ExtendedTableView
            {
                Intent = TableIntent.Settings,
                EnableScrolling = true,
                HasUnevenRows = true,
                Root = new TableRoot
                {
                    new TableSection(AppResources.ItemInformation)
                    {
                        NameCell,
                        UriCell,
                        UsernameCell,
                        PasswordCell,
                        GenerateCell
                    },
                    new TableSection(" ")
                    {
                        TotpCell,
                        FolderCell,
                        favoriteCell,
                        AttachmentsCell,
                        CustomFieldsCell
                    },
                    new TableSection(AppResources.Notes)
                    {
                        NotesCell
                    },
                    new TableSection(" ")
                    {
                        DeleteCell
                    }
                }
            };

            if(Device.RuntimePlatform == Device.iOS)
            {
                table.RowHeight = -1;
                table.EstimatedRowHeight = 70;
            }

            var saveToolBarItem = new ToolbarItem(AppResources.Save, null, async () =>
            {
                if(_lastAction.LastActionWasRecent())
                {
                    return;
                }
                _lastAction = DateTime.UtcNow;

                if(!_connectivity.IsConnected)
                {
                    AlertNoConnection();
                    return;
                }

                if(string.IsNullOrWhiteSpace(NameCell.Entry.Text))
                {
                    await DisplayAlert(AppResources.AnErrorHasOccurred, string.Format(AppResources.ValidationFieldRequired,
                        AppResources.Name), AppResources.Ok);
                    return;
                }

                cipher.Name = NameCell.Entry.Text.Encrypt(cipher.OrganizationId);
                cipher.Notes = string.IsNullOrWhiteSpace(NotesCell.Editor.Text) ? null :
                    NotesCell.Editor.Text.Encrypt(cipher.OrganizationId);
                cipher.Favorite = favoriteCell.On;

                cipher.Login = new Models.Login
                {
                    Uri = string.IsNullOrWhiteSpace(UriCell.Entry.Text) ? null :
                        UriCell.Entry.Text.Encrypt(cipher.OrganizationId),
                    Username = string.IsNullOrWhiteSpace(UsernameCell.Entry.Text) ? null :
                        UsernameCell.Entry.Text.Encrypt(cipher.OrganizationId),
                    Password = string.IsNullOrWhiteSpace(PasswordCell.Entry.Text) ? null :
                        PasswordCell.Entry.Text.Encrypt(cipher.OrganizationId),
                    Totp = string.IsNullOrWhiteSpace(TotpCell.Entry.Text) ? null :
                        TotpCell.Entry.Text.Encrypt(cipher.OrganizationId)
                };

                if(FolderCell.Picker.SelectedIndex > 0)
                {
                    cipher.FolderId = folders.ElementAt(FolderCell.Picker.SelectedIndex - 1).Id;
                }
                else
                {
                    cipher.FolderId = null;
                }

                _userDialogs.ShowLoading(AppResources.Saving, MaskType.Black);
                var saveTask = await _cipherService.SaveAsync(cipher);

                _userDialogs.HideLoading();

                if(saveTask.Succeeded)
                {
                    _userDialogs.Toast(AppResources.ItemUpdated);
                    _googleAnalyticsService.TrackAppEvent("EditedLogin");
                    await Navigation.PopForDeviceAsync();
                }
                else if(saveTask.Errors.Count() > 0)
                {
                    await _userDialogs.AlertAsync(saveTask.Errors.First().Message, AppResources.AnErrorHasOccurred);
                }
                else
                {
                    await _userDialogs.AlertAsync(AppResources.AnErrorHasOccurred);
                }
            }, ToolbarItemOrder.Default, 0);

            Title = AppResources.EditItem;
            Content = table;
            ToolbarItems.Add(saveToolBarItem);
            if(Device.RuntimePlatform == Device.iOS || Device.RuntimePlatform == Device.Windows)
            {
                ToolbarItems.Add(new DismissModalToolBarItem(this, AppResources.Cancel));
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if(!_connectivity.IsConnected)
            {
                AlertNoConnection();
            }

            PasswordCell?.InitEvents();
            UsernameCell?.InitEvents();
            UriCell?.InitEvents();
            NameCell?.InitEvents();
            NotesCell?.InitEvents();
            TotpCell?.InitEvents();
            FolderCell?.InitEvents();

            if(PasswordCell?.Button != null)
            {
                PasswordCell.Button.Clicked += PasswordButton_Clicked;
            }
            if(TotpCell?.Button != null)
            {
                TotpCell.Button.Clicked += TotpButton_Clicked;
            }
            if(GenerateCell != null)
            {
                GenerateCell.Tapped += GenerateCell_Tapped;
            }
            if(AttachmentsCell != null)
            {
                AttachmentsCell.Tapped += AttachmentsCell_Tapped;
            }
            if(CustomFieldsCell != null)
            {
                CustomFieldsCell.Tapped += CustomFieldsCell_Tapped;
            }
            if(DeleteCell != null)
            {
                DeleteCell.Tapped += DeleteCell_Tapped;
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            PasswordCell?.Dispose();
            TotpCell?.Dispose();
            UsernameCell?.Dispose();
            UriCell?.Dispose();
            NameCell?.Dispose();
            NotesCell?.Dispose();
            FolderCell?.Dispose();

            if(PasswordCell?.Button != null)
            {
                PasswordCell.Button.Clicked -= PasswordButton_Clicked;
            }
            if(TotpCell?.Button != null)
            {
                TotpCell.Button.Clicked -= TotpButton_Clicked;
            }
            if(GenerateCell != null)
            {
                GenerateCell.Tapped -= GenerateCell_Tapped;
            }
            if(AttachmentsCell != null)
            {
                AttachmentsCell.Tapped -= AttachmentsCell_Tapped;
            }
            if(CustomFieldsCell != null)
            {
                CustomFieldsCell.Tapped -= CustomFieldsCell_Tapped;
            }
            if(DeleteCell != null)
            {
                DeleteCell.Tapped -= DeleteCell_Tapped;
            }
        }

        private void PasswordButton_Clicked(object sender, EventArgs e)
        {
            PasswordCell.Entry.InvokeToggleIsPassword();
            PasswordCell.Button.Image = "eye" + (!PasswordCell.Entry.IsPasswordFromToggled ? "_slash" : string.Empty);
        }

        private async void TotpButton_Clicked(object sender, EventArgs e)
        {
            var scanPage = new ScanPage((key) =>
            {
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await Navigation.PopModalAsync();
                    if(!string.IsNullOrWhiteSpace(key))
                    {
                        TotpCell.Entry.Text = key;
                        _userDialogs.Toast(AppResources.AuthenticatorKeyAdded);
                    }
                    else
                    {
                        _userDialogs.Alert(AppResources.AuthenticatorKeyReadError);
                    }
                });
            });

            await Navigation.PushModalAsync(new ExtendedNavigationPage(scanPage));
        }

        private async void GenerateCell_Tapped(object sender, EventArgs e)
        {
            if(!string.IsNullOrWhiteSpace(PasswordCell.Entry.Text)
                && !await _userDialogs.ConfirmAsync(AppResources.PasswordOverrideAlert, null, AppResources.Yes, AppResources.No))
            {
                return;
            }

            var page = new ToolsPasswordGeneratorPage((password) =>
            {
                PasswordCell.Entry.Text = password;
                _userDialogs.Toast(AppResources.PasswordGenerated);
            });
            await Navigation.PushForDeviceAsync(page);
        }

        private async void AttachmentsCell_Tapped(object sender, EventArgs e)
        {
            var page = new ExtendedNavigationPage(new VaultAttachmentsPage(_cipherId));
            await Navigation.PushModalAsync(page);
        }

        private async void CustomFieldsCell_Tapped(object sender, EventArgs e)
        {
            var page = new ExtendedNavigationPage(new VaultCustomFieldsPage(_cipherId));
            await Navigation.PushModalAsync(page);
        }

        private async void DeleteCell_Tapped(object sender, EventArgs e)
        {
            if(!_connectivity.IsConnected)
            {
                AlertNoConnection();
                return;
            }

            if(!await _userDialogs.ConfirmAsync(AppResources.DoYouReallyWantToDelete, null, AppResources.Yes, AppResources.No))
            {
                return;
            }

            _userDialogs.ShowLoading(AppResources.Deleting, MaskType.Black);
            var deleteTask = await _cipherService.DeleteAsync(_cipherId);
            _userDialogs.HideLoading();

            if(deleteTask.Succeeded)
            {
                _userDialogs.Toast(AppResources.ItemDeleted);
                _googleAnalyticsService.TrackAppEvent("DeletedLogin");
                await Navigation.PopForDeviceAsync();
            }
            else if(deleteTask.Errors.Count() > 0)
            {
                await _userDialogs.AlertAsync(deleteTask.Errors.First().Message, AppResources.AnErrorHasOccurred);
            }
            else
            {
                await _userDialogs.AlertAsync(AppResources.AnErrorHasOccurred);
            }
        }

        private void AlertNoConnection()
        {
            DisplayAlert(AppResources.InternetConnectionRequiredTitle, AppResources.InternetConnectionRequiredMessage,
                AppResources.Ok);
        }
    }
}
