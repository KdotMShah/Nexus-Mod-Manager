﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;
using Nexus.Client.Commands.Generic;
using Nexus.Client.TipsManagement;
using System.Text;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.BackgroundTasks.UI;
using Nexus.Client.Commands;
using Nexus.Client.DownloadMonitoring.UI;
using Nexus.UI.Controls;
using Nexus.Client.Games;
using Nexus.Client.Games.Tools;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.UI;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement.UI;
using Nexus.Client.Settings.UI;
using Nexus.Client.UI;
using Nexus.Client.Util;
using WeifenLuo.WinFormsUI.Docking;
using System.Diagnostics;

namespace Nexus.Client
{
	/// <summary>
	/// The main form of the mod manager.
	/// </summary>
	public partial class MainForm : ManagedFontForm
	{
		private MainFormVM m_vmlViewModel = null;
		private FormWindowState m_fwsLastWindowState = FormWindowState.Normal;
		private ModManagerControl mmgModManager = null;
		private PluginManagerControl pmcPluginManager = null;
		private DownloadMonitorControl dmcDownloadMonitor = null;
		private ProfileManagerControl pmcProfileManager = null;
		private double m_dblDefaultActivityManagerAutoHidePortion = 0;
		public string strOptionalPremiumMessage = string.Empty;
		private bool m_booIsSwitching = false;

		private ToolStripMenuItem tmiShowTips = null;

		private System.Windows.Forms.TextBox caption;
		private System.Windows.Forms.TextBox content;
		private System.Windows.Forms.Label anchor;
		private System.Windows.Forms.Button showForm;

		private int pointX = 0;
		private int pointY = 0;
		FormWindowState LastWindowState = FormWindowState.Minimized;
		private bool m_booShowLastBaloon = false;
		private BalloonManager bmBalloon = null;

		private string m_strSelectedTipsVersion = String.Empty;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected MainFormVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;

				m_vmlViewModel.ProfileManager.ModProfiles.CollectionChanged += new NotifyCollectionChangedEventHandler(ModProfiles_CollectionChanged);
				m_vmlViewModel.ProfileSwitching += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_ProfileSwitching);
				m_vmlViewModel.ModManager.ActiveMods.CollectionChanged += new NotifyCollectionChangedEventHandler(ActiveMods_CollectionChanged);
				m_vmlViewModel.ModManager.VirtualModActivator.ModActivationChanged += new EventHandler(VirtualModActivator_ModActivationChanged);
				if (ViewModel.GameMode.UsesPlugins)
					m_vmlViewModel.PluginManager.ActivePlugins.CollectionChanged += new NotifyCollectionChangedEventHandler(ActivePlugins_CollectionChanged);

				mmgModManager.ViewModel = m_vmlViewModel.ModManagerVM;
				if (ViewModel.UsesPlugins)
					pmcPluginManager.ViewModel = m_vmlViewModel.PluginManagerVM;
				dmcDownloadMonitor.ViewModel = m_vmlViewModel.DownloadMonitorVM;
				dmcDownloadMonitor.ViewModel.ActiveTasks.CollectionChanged += new NotifyCollectionChangedEventHandler(ActiveTasks_CollectionChanged);
				dmcDownloadMonitor.ViewModel.Tasks.CollectionChanged += new NotifyCollectionChangedEventHandler(Tasks_CollectionChanged);
				dmcDownloadMonitor.ViewModel.PropertyChanged += new System.ComponentModel.PropertyChangedEventHandler(ActiveTasks_PropertyChanged);
				pmcProfileManager.ViewModel = m_vmlViewModel.ProfileManagerVM;

				this.ViewModel.ModRepository.UserStatusUpdate += new EventHandler(ModRepository_UserStatusUpdate);

				ApplyTheme(m_vmlViewModel.ModeTheme);

				Text = m_vmlViewModel.Title;

				m_vmlViewModel.ConfirmUpdaterAction = ConfirmUpdaterAction;

				foreach (HelpInformation.HelpLink hlpLink in m_vmlViewModel.HelpInfo.HelpLinks)
				{
					ToolStripMenuItem tmiHelp = new ToolStripMenuItem();
					tmiHelp.Tag = hlpLink;
					tmiHelp.Text = hlpLink.Name;
					tmiHelp.ToolTipText = hlpLink.Url;
					tmiHelp.ImageScaling = ToolStripItemImageScaling.None;
					tmiHelp.Click += new EventHandler(tmiHelp_Click);
					spbHelp.DropDownItems.Add(tmiHelp);
				}

				bmBalloon = new BalloonManager();
				bmBalloon.ShowNextClick += bmBalloon_ShowNextClick;
				bmBalloon.ShowPreviousClick += bmBalloon_ShowPreviousClick;
				bmBalloon.CloseClick += bmBalloon_CloseClick;

				BindCommands();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view with its dependencies.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		public MainForm(MainFormVM p_vmlViewModel)
		{

			InitializeComponent();

			this.FormClosing += new FormClosingEventHandler(this.CheckDownloadsOnClosing);

			this.ResizeEnd += MainForm_ResizeEnd;
			this.ResizeBegin += MainForm_ResizeBegin;
			this.Resize += MainForm_Resize;

			pmcPluginManager = new PluginManagerControl();
			mmgModManager = new ModManagerControl();
			dmcDownloadMonitor = new DownloadMonitorControl();
			pmcProfileManager = new ProfileManagerControl();
			dockPanel1.ActiveContentChanged += new EventHandler(dockPanel1_ActiveContentChanged);
			mmgModManager.SetTextBoxFocus += new EventHandler(mmgModManager_SetTextBoxFocus);
			mmgModManager.ResetSearchBox += new EventHandler(mmgModManager_ResetSearchBox);
			dmcDownloadMonitor.SetTextBoxFocus += new EventHandler(dmcDownloadMonitor_SetTextBoxFocus);
			p_vmlViewModel.ModManager.LoginTask.PropertyChanged += new PropertyChangedEventHandler(LoginTask_PropertyChanged);
			tsbTips.DropDownItemClicked += new ToolStripItemClickedEventHandler(tsbTips_DropDownItemClicked);

			ViewModel = p_vmlViewModel;

			try
			{
				InitializeDocuments();
			}
			catch
			{
				ResetUI();
			}

			p_vmlViewModel.EnvironmentInfo.Settings.WindowPositions.GetWindowPosition("MainForm", this);
			m_fwsLastWindowState = WindowState;

			if (ViewModel.RequiresModMigration())
			{
				string strMigrationWarning = "This new version of NMM includes a major update to the way we store and install your mods which allows us to accommodate" + Environment.NewLine +
					" mod profiling (different profiles for different playthroughs of your game)." + Environment.NewLine +
					"In order for it to work NMM needs to uninstall all your currently installed mods." + Environment.NewLine + Environment.NewLine +
					"Choose option 'YES' if you would like NMM to attempt to try and reinstall all your currently installed mods using the new method. " + Environment.NewLine +
					"The migration procedure is a lengthy process and it could require several minutes depending on your PC speed and quantity and size " + Environment.NewLine +
					"of your currently installed mods." + Environment.NewLine + "You will also be required to interact with ALL the scripted installers during the reinstall process." + Environment.NewLine + Environment.NewLine +
					"Choose option 'NO' if you want NMM to uninstall all your mods and leave you to activate the ones you use again " + Environment.NewLine +
					"(this doesn't delete your mods, it simply deactivates them)" + Environment.NewLine + Environment.NewLine +
					"Choose the 'CANCEL' option if you would like to cancel this setup and not proceed with this new version. You will not be upgraded to this version of NMM" + Environment.NewLine;

				DialogResult drResult = ExtendedMessageBox.Show(this, strMigrationWarning, "New version setup", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

				if (drResult == DialogResult.Cancel)
					Environment.Exit(0);
				else
				{
					byte[] bteLoadOrder = null;
					byte[] bteModList = null;
					int intModCount = -1;
					if (drResult == DialogResult.Yes)
					{
						if (ViewModel.GameMode.UsesPlugins)
							bteLoadOrder = ViewModel.PluginManagerVM.ExportLoadOrder();
						bteModList = ViewModel.ModManager.InstallationLog.GetXMLModList();
						intModCount = ViewModel.ModManager.ActiveMods.Count;
					}

					UninstallAllMods(true);

					ViewModel.ModManager.VirtualModActivator.Setup();
					if (drResult == DialogResult.Yes)
					{
						AddNewProfile(bteModList, bteLoadOrder, intModCount);
						SwitchProfile(ViewModel.ProfileManager.CurrentProfile, true);
					}
				}
			}
		}

		#endregion

		/// <summary>
		/// Initializes the main UI components.
		/// </summary>
		/// <remarks>
		/// If the metrics of the various UI components have been saved, they are loaded. Otherwise,
		/// the default layout is applied.
		/// </remarks>
		protected void InitializeDocuments()
		{
			string strTab = null;

			if (ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey("mainForm") && !String.IsNullOrEmpty(ViewModel.EnvironmentInfo.Settings.DockPanelLayouts["mainForm"]))
			{
				dockPanel1.LoadFromXmlString(ViewModel.EnvironmentInfo.Settings.DockPanelLayouts["mainForm"], LoadDockedContent);
				try
				{
					if (m_dblDefaultActivityManagerAutoHidePortion == 0)
						m_dblDefaultActivityManagerAutoHidePortion = dmcDownloadMonitor.AutoHidePortion;
				}
				catch { }
				if (!ViewModel.UsesPlugins)
					pmcPluginManager.Hide();
				if (!pmcProfileManager.Visible)
					pmcProfileManager.Show(dockPanel1, DockState.Document);
			}
			else
			{
				if (ViewModel.UsesPlugins)
					pmcPluginManager.DockState = DockState.Unknown;
				mmgModManager.DockState = DockState.Unknown;
				pmcProfileManager.DockState = DockState.Unknown;
				dmcDownloadMonitor.DockState = DockState.Unknown;
				dmcDownloadMonitor.ShowHint = DockState.DockBottomAutoHide;
				dmcDownloadMonitor.Show(dockPanel1, DockState.DockBottomAutoHide);
				if (m_dblDefaultActivityManagerAutoHidePortion == 0)
					m_dblDefaultActivityManagerAutoHidePortion = dmcDownloadMonitor.Height;
				try
				{
					dmcDownloadMonitor.AutoHidePortion = m_dblDefaultActivityManagerAutoHidePortion;
				}
				catch { }

				if (ViewModel.UsesPlugins)
					pmcPluginManager.Show(dockPanel1);
				mmgModManager.Show(dockPanel1);
			}

			strTab = dockPanel1.ActiveDocument.DockHandler.TabText;

			if (ViewModel.PluginManagerVM != null)
				pmcPluginManager.Show(dockPanel1);

			if ((ViewModel.UsesPlugins) && (strTab == "Plugins"))
				pmcPluginManager.Show(dockPanel1);
			else
				mmgModManager.Show(dockPanel1);

			if ((dmcDownloadMonitor == null) || ((dmcDownloadMonitor.VisibleState == DockState.Unknown) || (dmcDownloadMonitor.VisibleState == DockState.Hidden)))
			{
				dmcDownloadMonitor.Show(dockPanel1, DockState.DockBottomAutoHide);
				if (m_dblDefaultActivityManagerAutoHidePortion == 0)
					m_dblDefaultActivityManagerAutoHidePortion = dmcDownloadMonitor.Height;
				try
				{
					dmcDownloadMonitor.AutoHidePortion = m_dblDefaultActivityManagerAutoHidePortion;
				}
				catch { }
			}

			UserStatusFeedback();
		}

		/// <summary>
		/// The function that checks the Tips.
		/// </summary>
		protected void LoadTips()
		{
			bmBalloon.CheckTips(this.Location.X + tsbTips.Bounds.Location.X, this.Location.Y + tsbTips.Bounds.Location.Y, ViewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup, ProgrammeMetadata.VersionString);
		}

		/// <summary>
		/// Shows the tips.
		/// </summary>
		/// <param name="p_strVersion">The version of the DropDownMenu clicked</param>
		public void ShowTips(string p_strVersion)
		{
			if (!String.IsNullOrEmpty(p_strVersion))
				bmBalloon.SetTipList(p_strVersion);
			string strTipSection = String.IsNullOrEmpty(bmBalloon.TipSection) ? "toolStrip1" : bmBalloon.TipSection;
			string strTipObject = String.IsNullOrEmpty(bmBalloon.TipObject) ? "tsbTips" : bmBalloon.TipObject;
			bmBalloon.ShowNextTip(FindControlCoords(strTipSection, strTipObject));
		}

		/// <summary>
		/// The BalloonManager ShowNextClick event.
		/// </summary>
		void bmBalloon_ShowNextClick(object sender, EventArgs e)
		{
			if (m_vmlViewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup)
			{
				m_vmlViewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup = false;
				m_vmlViewModel.EnvironmentInfo.Settings.Save();
			}

			if (bmBalloon.CurrentTip == null)
				ShowTips(m_vmlViewModel.EnvironmentInfo.ApplicationVersion.ToString());
			else
				ShowTips(String.Empty);
		}

		/// <summary>
		/// The BalloonManager ShowPreviousClick event.
		/// </summary>
		void bmBalloon_ShowPreviousClick(object sender, EventArgs e)
		{
			ShowTips(String.Empty);
		}

		/// <summary>
		/// The BalloonManager CloseClick event.
		/// </summary>
		void bmBalloon_CloseClick(object sender, EventArgs e)
		{
			if (m_vmlViewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup)
			{
				m_vmlViewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup = false;
				m_vmlViewModel.EnvironmentInfo.Settings.Save();
			}
		}


		/// <summary>
		/// Sets the UI elements providing feedback on the user online status.
		/// </summary>
		protected void UserStatusFeedback()
		{

			if (ViewModel.OfflineMode)
			{
				if (tpbDownloadSpeed != null)
					tpbDownloadSpeed.Visible = false;
				tlbLoginMessage.Visible = true;
				tlbLoginMessage.Text = "You are not logged in.";
				tlbLoginMessage.Font = new Font(base.Font, FontStyle.Bold);
				tsbGoPremium.Visible = false;
				tsbOnlineStatus.Image = new Bitmap(Properties.Resources.offline_icon, 36, 34);
				tlbDownloads.Visible = false;
			}
			else
			{
				tsbOnlineStatus.Image = new Bitmap(Properties.Resources.online_icon, 36, 34);
				Int32 UserStatus = (ViewModel.UserStatus == null) || String.IsNullOrEmpty(ViewModel.UserStatus[1]) ? 3 : Convert.ToInt32(ViewModel.UserStatus[1]);

				if ((UserStatus != 4) && (UserStatus != 6) && (UserStatus != 13) && (UserStatus != 27) && (UserStatus != 31) && (UserStatus != 32))
				{
					tlbLoginMessage.Visible = true;
					tsbGoPremium.Visible = true;
					tsbGoPremium.Enabled = true;
					strOptionalPremiumMessage = " Not a Premium Member.";
					if (tpbDownloadSpeed != null)
					{
						tpbDownloadSpeed.Maximum = 1024;
						tpbDownloadSpeed.Value = 0;
						tpbDownloadSpeed.ColorFillMode = Nexus.Client.UI.Controls.ProgressLabel.FillType.Descending;
						tpbDownloadSpeed.ShowOptionalProgress = false;
					}
					tlbDownloads.Tag = "Download Speed:";
				}
				else
				{
					tlbLoginMessage.Visible = true;
					tsbGoPremium.Visible = false;
					strOptionalPremiumMessage = string.Empty;
					tsbGoPremium.Enabled = false;
					if (tpbDownloadSpeed != null)
					{
						tpbDownloadSpeed.Maximum = 100;
						tpbDownloadSpeed.Value = 0;
						tpbDownloadSpeed.ColorFillMode = Nexus.Client.UI.Controls.ProgressLabel.FillType.Ascending;
						tpbDownloadSpeed.ShowOptionalProgress = true;
					}
					tlbDownloads.Tag = "Download Progress:";
				}
				if ((tpbDownloadSpeed != null) && (dmcDownloadMonitor.ViewModel.ActiveTasks.Count > 0))
					tpbDownloadSpeed.Visible = true;
				tlbDownloads.Text = String.Format("{0} ({1} {2}) ", tlbDownloads.Tag, dmcDownloadMonitor.ViewModel.ActiveTasks.Count, (dmcDownloadMonitor.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files"));
			}
		}

		/// <summary>
		/// Resets the UI layout to the default.
		/// </summary>
		protected void ResetUI()
		{
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove("mainForm");
			InitializeDocuments();
			try
			{
				mmgModManager.ResetColumns();
			}
			catch { }
		}

		/// <summary>
		/// Disable all active mods.
		/// </summary>
		protected void DisableAllMods()
		{
			mmgModManager.DisableAllMods();
		}

		/// <summary>
		/// Uninstall all active mods.
		/// </summary>
		protected void UninstallAllMods()
		{
			UninstallAllMods(false);
		}

 		/// <summary>
		/// Uninstall all active mods.
		/// </summary>
		protected void UninstallAllMods(bool p_booForceUninstall)
		{
			mmgModManager.DeactivateAllMods(p_booForceUninstall);
 		}

		private void LoginTask_PropertyChanged(object sender, EventArgs e)
		{
			LoginFormTask lftTask = (LoginFormTask)sender;
			if ((lftTask.OverallMessage == "Logged in.") && (strOptionalPremiumMessage != string.Empty))
				tlbLoginMessage.Text = lftTask.OverallMessage + strOptionalPremiumMessage;
			else
				tlbLoginMessage.Text = lftTask.OverallMessage;
		}

		/// <summary>
		/// Opens the selected game folder.
		/// </summary>
		protected void OpenGameFolder()
		{
			if (FileUtil.IsValidPath(ViewModel.GamePath))
				System.Diagnostics.Process.Start(ViewModel.GamePath);
		}

		/// <summary>
		/// Checks if there are any active downloads before closing the mod manager.
		/// </summary>
		/// <remarks>
		/// If there's an active download, the program will ask the user if he really wants to close it.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="FormClosingEventArgs"/> describing the event arguments.</param>
		private void CheckDownloadsOnClosing(object sender, FormClosingEventArgs e)
		{
			if (this.ViewModel.DownloadMonitorVM.ActiveTasks.Count > 0)
			{
				DialogResult drFormClose = MessageBox.Show(String.Format("There is an ongoing download, are you sure you want to close {0}?", Application.ProductName), "Closing", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
				if (drFormClose != DialogResult.Yes)
					e.Cancel = true;
			}
		}

		private void MainForm_ResizeEnd(object sender, EventArgs e)
		{
			if ((ViewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup) && (bmBalloon.balloonHelp != null))
			{
				bmBalloon.balloonHelp.Close();
				bmBalloon.CheckTips(this.Location.X + tsbTips.Bounds.Location.X, this.Location.Y + tsbTips.Bounds.Location.Y, ViewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup, ProgrammeMetadata.VersionString);
			}
			else
			{
				if (m_booShowLastBaloon)
				{
					m_booShowLastBaloon = false;
					ShowTips(String.Empty);
				}
			}
		}

		private void MainForm_ResizeBegin(object sender, EventArgs e)
		{
			try
			{
				if (bmBalloon.balloonHelp.Visible)
				{
					if (bmBalloon.CurrentTip != null)
						bmBalloon.SetPreviousTip(true);
					bmBalloon.balloonHelp.Close();
					m_booShowLastBaloon = true;
				}
				else
					m_booShowLastBaloon = false;
			}
			catch { }
		}

		private void MainForm_Resize(object sender, EventArgs e)
		{
			if (WindowState != LastWindowState)
			{
				LastWindowState = WindowState;

				if ((WindowState == FormWindowState.Maximized) || (WindowState == FormWindowState.Normal))
				{
					if ((bmBalloon != null) && (bmBalloon.balloonHelp != null) && (bmBalloon.balloonHelp.Visible))
					{
						if (bmBalloon.CurrentTip != null)
						{
							bmBalloon.SetPreviousTip(true);
							ShowTips(String.Empty);
						}
						else
						{
							bmBalloon.balloonHelp.Close();
							bmBalloon.CheckTips(this.Location.X + tsbTips.Bounds.Location.X, this.Location.Y + tsbTips.Bounds.Location.Y, ViewModel.EnvironmentInfo.Settings.CheckForTipsOnStartup, ProgrammeMetadata.VersionString);
						}
					}
				}
			}
		}

		/// <summary>
		/// This will check whether the SearchBox should be visible.
		/// </summary>
		private void dockPanel1_ActiveContentChanged(object sender, EventArgs e)
		{
			if ((this.Visible) && (dockPanel1.ActiveDocument != null))
			{
				tstFind.Visible = (dockPanel1.ActiveDocument.DockHandler.TabText == "Mods");
				tstFind.Enabled = (dockPanel1.ActiveDocument.DockHandler.TabText == "Mods");
			}
		}

		/// <summary>
		/// Set the focus to the Search Textbox.
		/// </summary>
		private void mmgModManager_SetTextBoxFocus(object sender, EventArgs e)
		{
			tstFind.Focus();
		}

		private void mmgModManager_ResetSearchBox(object sender, EventArgs e)
		{
			tstFind.Clear();
		}

		/// <summary>
		/// Set the focus to the Search Textbox.
		/// </summary>
		private void dmcDownloadMonitor_SetTextBoxFocus(object sender, EventArgs e)
		{
			if (mmgModManager.Visible)
				tstFind.Focus();
		}

		/// <summary>
		/// Opens NMM's mods folder for the current game.
		/// </summary>
		protected void OpenModsFolder()
		{
			if (FileUtil.IsValidPath(ViewModel.ModsPath))
				System.Diagnostics.Process.Start(ViewModel.ModsPath);
		}

		private void tstFind_KeyUp(object sender, KeyEventArgs e)
		{
			mmgModManager.FindItemWithText(this.tstFind.Text);
		}

		/// <summary>
		/// Opens NMM's install info folder for the current game.
		/// </summary>
		protected void OpenInstallFolder()
		{
			if (FileUtil.IsValidPath(ViewModel.InstallInfoPath))
				System.Diagnostics.Process.Start(ViewModel.InstallInfoPath);
		}

		#region Binding Helpers

		/// <summary>
		/// Binds the commands to the UI.
		/// </summary>
		protected void BindCommands()
		{
			ViewModel.Updating += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_Updating);
			new ToolStripItemCommandBinding(tsbUpdate, ViewModel.UpdateCommand);

			ViewModel.LogoutCommand.BeforeExecute += new EventHandler<CancelEventArgs>(LogoutCommand_BeforeExecute);
			new ToolStripItemCommandBinding(tsbOnlineStatus, ViewModel.LogoutCommand);

			BindLaunchCommands();
			BindProfileCommands();
			BindSupportedToolsCommands();
			BindToolCommands();
			BindFolderCommands();
			BindChangeModeCommands();
		}

		#region Logout

		/// <summary>
		/// Handles the <see cref="Command.BeforeExecute"/> event of the logout command.
		/// </summary>
		/// <remarks>
		/// This confirms whether the user wants to logout.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		private void LogoutCommand_BeforeExecute(object sender, CancelEventArgs e)
		{
			if (!ViewModel.OfflineMode)
				if (ExtendedMessageBox.Show(this, "Do you want to logout? This will require you to authenticate using your username and password the next time you try to log in.", "Logout", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question) != System.Windows.Forms.DialogResult.Yes)
					e.Cancel = true;
		}

		#endregion

		#region Change Game Mode

		/// <summary>
		/// Handles the <see cref="Command.Executed"/> event of the change game mode command.
		/// </summary>
		/// <remarks>
		/// This closes the application.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void ChangeGameModeCommand_Executed(object sender, EventArgs e)
		{
			Close();
		}

		#endregion

		#region Tasks

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// active mod list.
		/// </summary>
		/// <remarks>
		/// This updates the list of mods to refelct changes to which mods are active.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void ActiveMods_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, NotifyCollectionChangedEventArgs>)ActiveMods_CollectionChanged, sender, e);
				return;
			}
			
			if ((ViewModel.ProfileManager.CurrentProfile != null) && !m_booIsSwitching)
			{
				string[] strOptionalFiles = null;
				if (ViewModel.GameMode.RequiresOptionalFilesCheckOnProfileSwitch)
					if ((ViewModel.PluginManager != null) && ((ViewModel.PluginManager.ActivePlugins != null) && (ViewModel.PluginManager.ActivePlugins.Count > 0)))
						strOptionalFiles = ViewModel.GameMode.GetOptionalFilesList(ViewModel.PluginManager.ActivePlugins.Select(x => x.Filename).ToArray());

				ViewModel.ProfileManager.UpdateProfile(ViewModel.ProfileManager.CurrentProfile, null, strOptionalFiles);
				BindProfileCommands();
			}
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// active mod list.
		/// </summary>
		/// <remarks>
		/// This updates the list of mods to refelct changes to which mods are active.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void ActivePlugins_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, NotifyCollectionChangedEventArgs>)ActivePlugins_CollectionChanged, sender, e);
				return;
			}

			if ((ViewModel.ProfileManager.CurrentProfile != null) && !m_booIsSwitching)
			{
				byte[] bteLoadOrder = null;
				if (ViewModel.GameMode.UsesPlugins)
					bteLoadOrder = ViewModel.PluginManagerVM.ExportLoadOrder();

				ViewModel.ProfileManager.UpdateProfileLoadOrder(ViewModel.ProfileManager.CurrentProfile, bteLoadOrder);
			}
		}

		/// <summary>
		/// Handles the <see cref="ModRepository.UserStatusUpdate"/> event of the tasks list.
		/// </summary>
		/// <remarks>
		/// Updates the UI elements.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ModRepository_UserStatusUpdate(object sender, EventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs>)ModRepository_UserStatusUpdate, sender, e);
				return;
			}
			UserStatusFeedback();
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the tasks list.
		/// </summary>
		/// <remarks>
		/// Displays the activity monitor.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void Tasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, NotifyCollectionChangedEventArgs>)Tasks_CollectionChanged, sender, e);
				return;
			}
			dmcDownloadMonitor.Activate();

			if (!ViewModel.OfflineMode)
			{
				tlbDownloads.Text = String.Format("{0} ({1} {2}) ", tlbDownloads.Tag, dmcDownloadMonitor.ViewModel.ActiveTasks.Count, (dmcDownloadMonitor.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files"));
				if (dmcDownloadMonitor.ViewModel.ActiveTasks.Count <= 0)
					UpdateProgressBarSpeed("TotalSpeed", true);
			}
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the active tasks list.
		/// </summary>
		/// <remarks>
		/// Displays the activity monitor.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void ActiveTasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, NotifyCollectionChangedEventArgs>)ActiveTasks_CollectionChanged, sender, e);
				return;
			}
			dmcDownloadMonitor.Activate();

			if (!ViewModel.OfflineMode)
			{
				if ((e.OldItems != null) && (e.OldItems.Count > 0))
				{
					foreach (AddModTask Task in e.OldItems)
						if (!String.IsNullOrEmpty(Task.ErrorCode) && (Task.ErrorCode == "666") && !((Task.Status == TaskStatus.Cancelling) || (Task.Status == TaskStatus.Cancelled) || (Task.Status == TaskStatus.Complete)))
						{
							MessageBox.Show(String.Format("The NMM web services have currently been disabled by staff of the sites."
								+ " This is NOT an error with NMM and you DO NOT need to report this error to us."
								+ " This is normally a temporary problem so please try again a bit later on in the day." + Environment.NewLine
								+ "If the staff have provided a reason for this down time we'll display it below: {0}", Environment.NewLine + Environment.NewLine + Task.ErrorInfo), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						}
				}
				tlbDownloads.Text = String.Format("{0} ({1} {2}) ", tlbDownloads.Tag, dmcDownloadMonitor.ViewModel.ActiveTasks.Count, (dmcDownloadMonitor.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files"));
				if (dmcDownloadMonitor.ViewModel.ActiveTasks.Count <= 0)
					UpdateProgressBarSpeed("TotalSpeed", true);
			}
		}

		/// <summary>
		/// Handles the <see cref="System.ComponentModel.ProgressChangedEventHandler"/> event of the active tasks list.
		/// </summary>
		/// <remarks>
		/// Checks the current downloading speed.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="System.ComponentModel.PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void ActiveTasks_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, System.ComponentModel.PropertyChangedEventArgs>)ActiveTasks_PropertyChanged, sender, e);
				return;
			}
			UpdateProgressBarSpeed(e.PropertyName, false);
		}

		/// <summary>
		/// Checks if the downloading speed progress bar needs to be updated.
		/// </summary>
		/// <param name="PropertyName">The property name.</param>
		/// <param name="OverrideSpeed">If true the speed value is overridden with a 0.</param>
		private void UpdateProgressBarSpeed(string PropertyName, bool OverrideSpeed)
		{
			if (tpbDownloadSpeed != null)
			{
				if ((tpbDownloadSpeed.IsValid) && ((PropertyName == "TotalSpeed") || (PropertyName == "TotalProgress")))
				{
					if (OverrideSpeed)
					{
						tpbDownloadSpeed.Value = 0;
						if ((tpbDownloadSpeed.ColorFillMode == Nexus.Client.UI.Controls.ProgressLabel.FillType.Fixed))
							tpbDownloadSpeed.Maximum = 1;
						tpbDownloadSpeed.Visible = false;
					}
					else if (tpbDownloadSpeed.ColorFillMode == Nexus.Client.UI.Controls.ProgressLabel.FillType.Fixed)
					{
						tpbDownloadSpeed.Visible = true;
						tpbDownloadSpeed.Maximum = dmcDownloadMonitor.ViewModel.TotalSpeed > 0 ? dmcDownloadMonitor.ViewModel.TotalSpeed : 1;
						tpbDownloadSpeed.Value = tpbDownloadSpeed.Maximum;
					}
					else if (tpbDownloadSpeed.ColorFillMode == Nexus.Client.UI.Controls.ProgressLabel.FillType.Ascending)
					{
						tpbDownloadSpeed.Visible = true;
						if (dmcDownloadMonitor.ViewModel.TotalMaxProgress > 0)
						{
							tpbDownloadSpeed.Value = Convert.ToInt32((Convert.ToSingle(dmcDownloadMonitor.ViewModel.TotalProgress) / Convert.ToSingle(dmcDownloadMonitor.ViewModel.TotalMaxProgress)) * 100);
							tpbDownloadSpeed.OptionalValue = dmcDownloadMonitor.ViewModel.TotalSpeed;
						}
					}
					else if (tpbDownloadSpeed.ColorFillMode == Nexus.Client.UI.Controls.ProgressLabel.FillType.Descending)
					{
						tpbDownloadSpeed.Visible = true;
						if (dmcDownloadMonitor.ViewModel.TotalSpeed <= 1024)
							tpbDownloadSpeed.Value = dmcDownloadMonitor.ViewModel.TotalSpeed;
						else
							tpbDownloadSpeed.Value = 1024;
					}
				}
			}
		}

		#endregion

		#endregion

		#region Control Metrics Serialization

		/// <summary>
		/// Raises the <see cref="Form.Closed"/> event of the form.
		/// </summary>
		/// <remarks>
		/// This saves the form's metrics.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
			if (!DesignMode)
			{
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts["mainForm"] = dockPanel1.SaveAsXml();
				ViewModel.EnvironmentInfo.Settings.Save();
			}
		}

		/// <summary>
		/// Returns the UI component being requested when the form's metrics are being loaded.
		/// </summary>
		/// <param name="p_strContentId">The id of the component to return to be positioned.</param>
		/// <returns>The component to return to be positioned.</returns>
		protected IDockContent LoadDockedContent(string p_strContentId)
		{
			if (p_strContentId == typeof(PluginManagerControl).ToString())
				return pmcPluginManager;
			else if (p_strContentId == typeof(ModManagerControl).ToString())
				return mmgModManager;
			else if (p_strContentId == typeof(DownloadMonitorControl).ToString())
				return dmcDownloadMonitor;
			else if (p_strContentId == typeof(ProfileManagerControl).ToString())
				return pmcProfileManager;
			else
				return null;
		}

		#endregion

		#region Maintenance Binding Helpers

		/// <summary>
		/// Handles the <see cref="MainFormVM.Updating"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_Updating(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_Updating, sender, e);
				return;
			}
			ProgressDialog.ShowDialog(this, e.Argument);
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the setting button.
		/// </summary>
		/// <remarks>Displays the settings form.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbSettings_Click(object sender, EventArgs e)
		{
			SettingsForm frmSettings = new SettingsForm(ViewModel.SettingsFormVM);
			if (frmSettings.ShowDialog(this) == DialogResult.OK)
			{
				mmgModManager.RefreshModList();
				if (ViewModel.SupportedToolsLauncher != null)
				{
					ViewModel.SupportedToolsLauncher.SetupCommands();
					BindSupportedToolsCommands();
				}
			}
		}

		/// <summary>
		/// This asks the user to confirm an updater action.
		/// </summary>
		/// <param name="p_strMessage">The message describing the action to confirm.</param>
		/// <param name="p_strTitle">The title of the action to confirm.</param>
		/// <returns><c>true</c> if the action has been confirmed;
		/// <c>false</c> otherwise.</returns>
		private bool ConfirmUpdaterAction(string p_strMessage, string p_strTitle)
		{
			if (InvokeRequired)
				return (bool)Invoke((ConfirmActionMethod)ConfirmUpdaterAction, p_strMessage, p_strTitle);
			return MessageBox.Show(this, p_strMessage, p_strTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
		}

		#endregion

		#region Change Game Mode Binding Helpers

		/// <summary>
		/// Binds the change game mode commands to the UI.
		/// </summary>
		protected void BindChangeModeCommands()
		{
			foreach (Command cmdChangeCommand in ViewModel.ChangeGameModeCommands)
			{
				cmdChangeCommand.Executed += new EventHandler(ChangeGameModeCommand_Executed);
				ToolStripMenuItem tmiChange = new ToolStripMenuItem();
				new ToolStripItemCommandBinding(tmiChange, cmdChangeCommand);
				spbChangeMode.DropDownItems.Add(tmiChange);
			}
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the change game mode button.
		/// </summary>
		/// <remarks>
		/// This displays the list of game modes when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbChangeMode_ButtonClick(object sender, EventArgs e)
		{
			spbChangeMode.DropDown.Show();
		}

		#endregion

		#region Tools Binding Helpers

		/// <summary>
		/// Binds the tool launch commands to the UI.
		/// </summary>
		protected void BindToolCommands()
		{
			Command cmdResetUI = new Command("Reset UI", "Resets the UI to the default layout.", ResetUI);
			ToolStripMenuItem tmiResetTool = new ToolStripMenuItem();
			tmiResetTool.ImageScaling = ToolStripItemImageScaling.None;
			new ToolStripItemCommandBinding(tmiResetTool, cmdResetUI);
			spbTools.DropDownItems.Add(tmiResetTool);

			Command cmdDisableAllMods = new Command("Disable all active mods", "Disables all active mods.", DisableAllMods);
			ToolStripMenuItem tmiDisableAllMods = new ToolStripMenuItem();
			tmiDisableAllMods.Image = global::Nexus.Client.Properties.Resources.edit_delete;
			new ToolStripItemCommandBinding(tmiDisableAllMods, cmdDisableAllMods);
			spbTools.DropDownItems.Add(tmiDisableAllMods);

			Command cmdUninstallAllMods = new Command("Uninstall all active mods", "Uninstalls all active mods.", UninstallAllMods);
			ToolStripMenuItem tmiUninstallAllMods = new ToolStripMenuItem();
			tmiUninstallAllMods.Image = global::Nexus.Client.Properties.Resources.edit_delete_6;
			new ToolStripItemCommandBinding(tmiUninstallAllMods, cmdUninstallAllMods);
			spbTools.DropDownItems.Add(tmiUninstallAllMods);

			IEnumerable<string> enuVersions = bmBalloon.GetVersionList();
			if (enuVersions != null)
			{
				foreach (string strVersion in enuVersions)
				{
					Command<string> cmdShowTips = new Command<string>(strVersion, "Shows the tips for the current version.", ShowTips);
					tmiShowTips = new ToolStripMenuItem();
					tmiShowTips.ImageScaling = ToolStripItemImageScaling.None;
					tmiShowTips.Image = global::Nexus.Client.Properties.Resources.tipsIcon;
					new ToolStripItemCommandBinding<string>(tmiShowTips, cmdShowTips, GetSelectedVersion);

					tsbTips.DropDownItems.Add(tmiShowTips);
				}
			}

			foreach (ITool tolTool in ViewModel.GameToolLauncher.Tools)
			{
				ToolStripMenuItem tmiTool = new ToolStripMenuItem();
				tmiTool.Tag = tolTool;
				tmiTool.ImageScaling = ToolStripItemImageScaling.None;
				new ToolStripItemCommandBinding(tmiTool, tolTool.LaunchCommand);
				tolTool.DisplayToolView += new EventHandler<DisplayToolViewEventArgs>(Tool_DisplayToolView);
				tolTool.CloseToolView += new EventHandler<DisplayToolViewEventArgs>(Tool_CloseToolView);
				spbTools.DropDownItems.Add(tmiTool);
			}
		}

		private void tsbTips_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			m_strSelectedTipsVersion = e.ClickedItem.Text;
		}

		private string GetSelectedVersion()
		{
			return m_strSelectedTipsVersion;
		}

		private void close_Click(object sender, System.EventArgs e)
		{
			Close();
		}

		/// <summary>
		/// Handles the <see cref="ITool.CloseToolView"/> event of a tool.
		/// </summary>
		/// <remarks>
		/// This closes the tool's view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="DisplayToolViewEventArgs"/> describing the event arguments.</param>
		private void Tool_CloseToolView(object sender, DisplayToolViewEventArgs e)
		{
			((Form)e.ToolView).Close();
		}

		/// <summary>
		/// Handles the <see cref="ITool.DisplayToolView"/> event of a tool.
		/// </summary>
		/// <remarks>
		/// This shows the tool's view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="DisplayToolViewEventArgs"/> describing the event arguments.</param>
		private void Tool_DisplayToolView(object sender, DisplayToolViewEventArgs e)
		{
			if (e.IsModal)
				((Form)e.ToolView).ShowDialog(this);
			else
				((Form)e.ToolView).Show(this);
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the tools button.
		/// </summary>
		/// <remarks>
		/// This displays the list of tools when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbTools_ButtonClick(object sender, EventArgs e)
		{
			spbTools.DropDown.Show();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the Go Premium button.
		/// </summary>
		/// <remarks>
		/// Opens a default browser window on the Premium webpage.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbGoPremium_Click(object sender, EventArgs e)
		{
			System.Diagnostics.Process.Start("http://skyrim.nexusmods.com/users/premium/");
		}

		private Point FindControlCoords(string p_section, string p_object)
		{
			Point pCoords = new Point(0, 0);
			ToolStripItem rootItem = null;
			Control root = null;

			switch (p_section)
			{
				case "toolStrip1":
					root = this.Controls.Find(p_section, true)[0];
					rootItem = ((ToolStrip)root).Items.Find(p_object, true)[0];
					pCoords.X = rootItem.AccessibilityObject.Bounds.Location.X - 10;
					pCoords.Y = rootItem.AccessibilityObject.Bounds.Location.Y - 30;
					break;

				case "tssDownload":
					root = this.Controls.Find(p_section, true)[0];
					rootItem = ((StatusStrip)root).Items.Find(p_object, true)[0];
					pCoords.X = rootItem.AccessibilityObject.Bounds.Location.X - 10;
					pCoords.Y = rootItem.AccessibilityObject.Bounds.Location.Y - 60;
					break;

				case "ModManager.toolStrip1":
					p_section = "toolStrip1";
					root = mmgModManager.Controls.Find(p_section, true)[0];
					rootItem = ((ToolStrip)root).Items.Find(p_object, true)[0];
					pCoords.X = rootItem.AccessibilityObject.Bounds.Location.X - 5;
					pCoords.Y = rootItem.AccessibilityObject.Bounds.Location.Y - 10;
					break;

				case "DownloadManager.toolStrip1":
					p_section = "toolStrip1";
					root = dmcDownloadMonitor.Controls.Find(p_section, true)[0];
					rootItem = ((ToolStrip)root).Items.Find(p_object, true)[0];

					switch (dmcDownloadMonitor.DockState)
					{
						case DockState.DockBottomAutoHide:
							dmcDownloadMonitor.DockState = DockState.DockBottom;
							break;
						case DockState.DockLeftAutoHide:
							dmcDownloadMonitor.DockState = DockState.DockLeft;
							break;
						case DockState.DockRightAutoHide:
							dmcDownloadMonitor.DockState = DockState.DockRight;
							break;
						case DockState.DockTopAutoHide:
							dmcDownloadMonitor.DockState = DockState.DockTop;
							break;
					}

					if (!dmcDownloadMonitor.Visible)
						dmcDownloadMonitor.Show();
					pCoords.X = rootItem.AccessibilityObject.Bounds.Location.X - 10;
					pCoords.Y = rootItem.AccessibilityObject.Bounds.Location.Y - 40;
					break;

				case "CLWCategoryListView":
					pCoords.X = mmgModManager.clwCategoryView.AccessibilityObject.Bounds.Location.X;
					pCoords.Y = mmgModManager.clwCategoryView.AccessibilityObject.Bounds.Location.Y - 40;
					break;
			}

			return pCoords;
		}

		#endregion

		#region Open Folders Helpers

		/// <summary>
		/// Binds the tool launch commands to the UI.
		/// </summary>
		protected void BindFolderCommands()
		{
			Command cmdGameFolder = new Command("Open Game Folder", "Open the game's root folder in the explorer window.", OpenGameFolder);
			ToolStripMenuItem tmiGameFolder = new ToolStripMenuItem();
			tmiGameFolder.ImageScaling = ToolStripItemImageScaling.None;
			new ToolStripItemCommandBinding(tmiGameFolder, cmdGameFolder);
			spbFolders.DropDownItems.Add(tmiGameFolder);

			Command cmdModsFolder = new Command("Open NMM's Mods Folder", "Open NMM's mods folder in the explorer window.", OpenModsFolder);
			ToolStripMenuItem tmiModsFolder = new ToolStripMenuItem();
			tmiModsFolder.ImageScaling = ToolStripItemImageScaling.None;
			new ToolStripItemCommandBinding(tmiModsFolder, cmdModsFolder);
			spbFolders.DropDownItems.Add(tmiModsFolder);

			Command cmdInstallFolder = new Command("Open NMM's Install Info Folder", "Open NMM's install info folder in the explorer window.", OpenInstallFolder);
			ToolStripMenuItem tmiInstallFolder = new ToolStripMenuItem();
			tmiInstallFolder.ImageScaling = ToolStripItemImageScaling.None;
			new ToolStripItemCommandBinding(tmiInstallFolder, cmdInstallFolder);
			spbFolders.DropDownItems.Add(tmiInstallFolder);
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the tools button.
		/// </summary>
		/// <remarks>
		/// This displays the list of tools when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbFolders_ButtonClick(object sender, EventArgs e)
		{
			spbFolders.DropDown.Show();
		}

		#endregion

		#region Help Links Binding Helpers

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the help button.
		/// </summary>
		/// <remarks>
		/// This displays the list of help items when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbHelp_ButtonClick(object sender, EventArgs e)
		{
			spbHelp.DropDown.Show();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the help links.
		/// </summary>
		/// <remarks>
		/// This launches the link in the user's browser.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tmiHelp_Click(object sender, EventArgs e)
		{
			HelpInformation.HelpLink hlpLink = (HelpInformation.HelpLink)((ToolStripMenuItem)sender).Tag;
			if (hlpLink == null)
				return;
			try
			{
				System.Diagnostics.Process.Start(hlpLink.Url);
			}
			catch (Win32Exception)
			{
				MessageBox.Show(this, "Cannot find programme to open: " + hlpLink.Url, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Trace.WriteLine("Cannot find programme to open: " + hlpLink.Url);
			}
		}

		#endregion

		#region Task Set Handling

		/// <summary>
		/// Handles the <see cref="MainFormVM.ProfileSwitching"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_ProfileSwitching(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ProfileSwitching, sender, e);
				return;
			}
			ProgressDialog.ShowDialog(this, e.Argument, false);

			if (ViewModel.GameMode.UsesPlugins)
				ViewModel.ProfilePluginImport();

			string strMessage;
			string strOptionalToolPath = ViewModel.GameMode.PostProfileSwitchTool(out strMessage);
			if ((!String.IsNullOrEmpty(strOptionalToolPath)) && (File.Exists(strOptionalToolPath)))
				if (ExtendedMessageBox.Show(this, strMessage, "Optional tool detected", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
					ViewModel.GameMode.SupportedToolsLauncher.LaunchDefaultCommand();

			m_booIsSwitching = false;
			ViewModel.ProfileManager.UpdateProfile(ViewModel.ProfileManager.CurrentProfile, null, null);
			ViewModel.ProfileManager.SetDefaultProfile(ViewModel.ProfileManager.CurrentProfile);	
			ViewModel.ProfileManager.SaveConfig();
			//mmgModManager.ForceListRefresh();
			BindProfileCommands();
		}

		private void VirtualModActivator_ModActivationChanged(object sender, EventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs>)VirtualModActivator_ModActivationChanged, sender, e);
				return;
			}

			if (!m_booIsSwitching)
			{
				if (ViewModel.ProfileManager.CurrentProfile != null)
				{
					string[] strOptionalFiles = null;
					if (ViewModel.GameMode.RequiresOptionalFilesCheckOnProfileSwitch)
						if ((ViewModel.PluginManager != null) && ((ViewModel.PluginManager.ActivePlugins != null) && (ViewModel.PluginManager.ActivePlugins.Count > 0)))
							strOptionalFiles = ViewModel.GameMode.GetOptionalFilesList(ViewModel.PluginManager.ActivePlugins.Select(x => x.Filename).ToArray());

					ViewModel.ProfileManager.UpdateProfile(ViewModel.ProfileManager.CurrentProfile, null, strOptionalFiles);
					mmgModManager.SetCommandExecutableStatus();
					BindProfileCommands();
				}
			}
		}

		#endregion

		#region Game Launch Binding Helpers

		/// <summary>
		/// Binds the game launch commands to the UI.
		/// </summary>
		protected void BindLaunchCommands()
		{
			foreach (Command cmdLaunch in ViewModel.GameLauncher.LaunchCommands)
			{
				ToolStripMenuItem tmiLaunch = new ToolStripMenuItem();
				tmiLaunch.Tag = cmdLaunch;
				new ToolStripItemCommandBinding(tmiLaunch, cmdLaunch);
				spbLaunch.DropDownItems.Add(tmiLaunch);
				if (String.Equals(cmdLaunch.Id, m_vmlViewModel.SelectedGameLaunchCommandId))
				{
					spbLaunch.DefaultItem = tmiLaunch;
					spbLaunch.Text = spbLaunch.DefaultItem.Text;
					spbLaunch.Image = spbLaunch.DefaultItem.Image;
				}
			}
			if (spbLaunch.DefaultItem == null)
			{
				if (spbLaunch.DropDownItems.Count > 0)
				{
					spbLaunch.DefaultItem = spbLaunch.DropDownItems[0];
					spbLaunch.Text = spbLaunch.DefaultItem.Text;
					spbLaunch.Image = spbLaunch.DefaultItem.Image;
				}
				else
				{
					spbLaunch.Text = "Launch Game";
					spbLaunch.Image = null;
					spbLaunch.Enabled = false;
				}
			}
			ViewModel.ConfirmCloseAfterGameLaunch = ConfirmCloseAfterGameLaunch;
			ViewModel.GameLauncher.GameLaunched += new EventHandler<GameLaunchEventArgs>(GameLauncher_GameLaunched);
		}

 		/// <summary>
		/// Binds the game profiles commands to the UI.
		/// </summary>
		protected void BindProfileCommands()
		{
			if (ViewModel.ProfileManager.Initialized)
			{
				spbProfiles.DropDownItems.Clear();
				spbProfiles.DefaultItem = null;
				ToolStripMenuItem tmiMenuItem = new ToolStripMenuItem();
				tmiMenuItem.Tag = "New";
				tmiMenuItem.Text = "New Profile";
				spbProfiles.DropDownItems.Add(tmiMenuItem);
				tmiMenuItem = new ToolStripMenuItem();
				tmiMenuItem.Tag = "Rename";
				tmiMenuItem.Text = "Rename Current Profile";
				spbProfiles.DropDownItems.Add(tmiMenuItem);
				tmiMenuItem = new ToolStripMenuItem();
				tmiMenuItem.Tag = "Remove";
				tmiMenuItem.Text = "Remove Current Profile";
				spbProfiles.DropDownItems.Add(tmiMenuItem);
				tmiMenuItem = new ToolStripMenuItem();
				tmiMenuItem.Tag = "Save";
				tmiMenuItem.Text = "Save Current Profile";
				spbProfiles.DropDownItems.Add(tmiMenuItem);
				spbProfiles.DropDownItems.Add(new ToolStripSeparator());

				foreach (IModProfile impProfile in ViewModel.ProfileManager.ModProfiles)
				{
					ToolStripMenuItem tmiProfile = new ToolStripMenuItem();
					tmiProfile.Tag = impProfile;
					tmiProfile.Text = impProfile.Name + " (" + impProfile.ModCount.ToString() + ")";
					spbProfiles.DropDownItems.Add(tmiProfile);

					if (impProfile.IsDefault)
					{
						spbProfiles.DefaultItem = tmiProfile;
						spbProfiles.Text = impProfile.Name;
						spbProfiles.Image = spbProfiles.Image;
					}
				}
				if (spbProfiles.DefaultItem == null)
				{
					if (spbProfiles.DropDownItems.Count > 0)
					{
						spbProfiles.DefaultItem = spbProfiles.DropDownItems[0];
						spbProfiles.Text = spbProfiles.DefaultItem.Text;
						spbProfiles.Image = spbProfiles.Image;
					}
				}
				spbProfiles.Visible = true;
			}
			else
				spbProfiles.Visible = false;
		}

		/// <summary>
		/// Binds the SupportedTools launch commands to the UI.
		/// </summary>
		protected void BindSupportedToolsCommands()
		{
			spbSupportedTools.DropDownItems.Clear();
			spbSupportedTools.DefaultItem = null;

			if (ViewModel.SupportedToolsLauncher != null)
			{
				foreach (Command cmdLaunch in ViewModel.SupportedToolsLauncher.LaunchCommands)
				{

					ToolStripMenuItem tmiLaunch = new ToolStripMenuItem();
					tmiLaunch.Tag = cmdLaunch;
					new ToolStripItemCommandBinding(tmiLaunch, cmdLaunch);
					spbSupportedTools.DropDownItems.Add(tmiLaunch);
				}

				if (spbLaunch.DefaultItem == null)
				{
					if (spbLaunch.DropDownItems.Count > 0)
					{
						spbLaunch.DefaultItem = spbLaunch.DropDownItems[0];
						spbLaunch.Text = spbLaunch.DefaultItem.Text;
						spbLaunch.Image = spbLaunch.DefaultItem.Image;
					}
				}
			}
			else
				spbSupportedTools.Visible = false;
		}

		/// <summary>
		/// Handles the <see cref="ToolStripDropDownItem.DropDownItemClicked"/> of the launch game
		/// split button.
		/// </summary>
		/// <remarks>
		/// This makes the last selected function the new default for the button.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ToolStripItemClickedEventArgs"/> describing the event arguments.</param>
		private void spbLaunch_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			spbLaunch.DefaultItem = e.ClickedItem;
			spbLaunch.Text = e.ClickedItem.Text;
			toolStrip1.SuspendLayout();
			spbLaunch.Image = e.ClickedItem.Image;
			toolStrip1.ResumeLayout();
			m_vmlViewModel.SelectedGameLaunchCommandId = ((Command)e.ClickedItem.Tag).Id;
		}

		/// <summary>
		/// Handles the <see cref="ToolStripDropDownItem.DropDownItemClicked"/> of the launch game
		/// split button.
		/// </summary>
		/// <remarks>
		/// This makes the last selected function the new default for the button.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ToolStripItemClickedEventArgs"/> describing the event arguments.</param>
		private void spbSupportedTools_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			spbSupportedTools.DefaultItem = e.ClickedItem;
			spbSupportedTools.Text = e.ClickedItem.Text;
			toolStrip1.SuspendLayout();
			spbSupportedTools.Image = e.ClickedItem.Image;
			toolStrip1.ResumeLayout();
		}

 		/// <summary>
		/// Handles the <see cref="ToolStripDropDownItem.DropDownItemClicked"/> of the profiles
		/// split button.
		/// </summary>
		/// <remarks>
		/// This makes the last selected function the new default for the button.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ToolStripItemClickedEventArgs"/> describing the event arguments.</param>
		private void spbProfiles_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			spbProfiles.DefaultItem = e.ClickedItem;
			spbProfiles.Text = e.ClickedItem.Text;
			toolStrip1.SuspendLayout();
			spbProfiles.Image = e.ClickedItem.Image;
			toolStrip1.ResumeLayout();

			if (e.ClickedItem.Tag.GetType() == typeof(string))
			{
				string strCommand = e.ClickedItem.Tag.ToString();
				switch (strCommand)
				{
					case "New":
						byte[] bteLoadOrder = null;
						if (ViewModel.GameMode.UsesPlugins)
							bteLoadOrder = ViewModel.PluginManagerVM.ExportLoadOrder();
						AddNewProfile(bteLoadOrder);
						ModProfile mopCurrentProfile = (ModProfile)ViewModel.ProfileManager.CurrentProfile;
						if (mopCurrentProfile != null)
						{
							string strNewName = PromptDialog.ShowDialog(this, "Type the profile name:", "Set the Profile name", mopCurrentProfile.Name);
							if (!String.IsNullOrEmpty(strNewName) && !strNewName.Equals(mopCurrentProfile.Name, StringComparison.InvariantCulture))
							{
								mopCurrentProfile.Name = strNewName;
								ViewModel.ProfileManager.UpdateProfile(mopCurrentProfile, null, null);
								//BindProfileCommands();
							}
						}
						break;
					case "Rename":
						ModProfile mopCurrent = (ModProfile)ViewModel.ProfileManager.CurrentProfile;
						if (mopCurrent != null)
						{
							string strNewName = PromptDialog.ShowDialog(this, "Type the new name:", "Rename Profile", mopCurrent.Name);
							if (!String.IsNullOrEmpty(strNewName) && !strNewName.Equals(mopCurrent.Name, StringComparison.InvariantCulture))
							{
								mopCurrent.Name = strNewName;
								ViewModel.ProfileManager.UpdateProfile(mopCurrent, null, null);
								BindProfileCommands();
							}
						}
						break;
					case "Remove":
						ModProfile mopProfile = (ModProfile)ViewModel.ProfileManager.CurrentProfile;
						if (mopProfile != null)
						{
							DialogResult drResult = ExtendedMessageBox.Show(this, String.Format("Are you sure you want to remove the current profile: {0}", mopProfile.Name), "Remove Profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
							if (drResult == DialogResult.Yes)
							{
								ViewModel.ProfileManager.RemoveProfile(mopProfile);
								//BindProfileCommands();
							}
						}
						break;
					case "Save":
						ModProfile mopUpdate = (ModProfile)ViewModel.ProfileManager.CurrentProfile;
						if (mopUpdate != null)
						{
							byte[] bteNewLoadOrder = null;
							if (ViewModel.GameMode.UsesPlugins)
								bteNewLoadOrder = ViewModel.PluginManagerVM.ExportLoadOrder();

							string[] strOptionalFiles = null;
							if (ViewModel.GameMode.RequiresOptionalFilesCheckOnProfileSwitch)
								if ((ViewModel.PluginManager != null) && ((ViewModel.PluginManager.ActivePlugins != null) && (ViewModel.PluginManager.ActivePlugins.Count > 0)))
									strOptionalFiles = ViewModel.GameMode.GetOptionalFilesList(ViewModel.PluginManager.ActivePlugins.Select(x => x.Filename).ToArray());

							ViewModel.ProfileManager.UpdateProfile(mopUpdate, bteNewLoadOrder, strOptionalFiles);
							BindProfileCommands();
						}
						break;
					default:
						break;
				}
			}
			else
			{
				IModProfile impProfile = (IModProfile)e.ClickedItem.Tag;

				if (impProfile != null)
				{
					SwitchProfile(impProfile, false);
				}
			}
		}

		private void ModProfiles_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			BindProfileCommands();
		}

		private void AddNewProfile(byte[] p_bteLoadOrder)
		{
			AddNewProfile(null, p_bteLoadOrder, -1);
		}

		private void AddNewProfile(byte[] p_bteModList, byte[] p_bteLoadOrder, int p_intModCount)
		{
			string[] strOptionalFiles = null;

			if (ViewModel.GameMode.RequiresOptionalFilesCheckOnProfileSwitch)
				if ((ViewModel.PluginManager != null) && ((ViewModel.PluginManager.ActivePlugins != null) && (ViewModel.PluginManager.ActivePlugins.Count > 0)))
					strOptionalFiles = ViewModel.GameMode.GetOptionalFilesList(ViewModel.PluginManager.ActivePlugins.Select(x => x.Filename).ToArray());
			IModProfile impProfile = ViewModel.ProfileManager.AddProfile(p_bteModList, p_bteLoadOrder, ViewModel.GameMode.ModeId, p_intModCount, strOptionalFiles);

			BindProfileCommands();
		}

		private void SwitchProfile(IModProfile p_impProfile, bool p_booSilentInstall)
		{
			if (p_booSilentInstall || (ViewModel.ProfileManager.CurrentProfile == null) || (p_impProfile.Id != ViewModel.ProfileManager.CurrentProfile.Id))			
			{
				List<IVirtualModLink> lstVirtualLinks = new List<IVirtualModLink>();
				Dictionary<string, string> dicProfile = null;
				Dictionary<string, string> dicNotFound = new Dictionary<string, string>();

				if ((p_impProfile.ModFileList != null) && (p_impProfile.ModFileList.Count > 0))
				{
					lstVirtualLinks = p_impProfile.ModFileList;
					dicNotFound = ViewModel.ModManager.VirtualModActivator.CheckLinkListIntegrity(lstVirtualLinks);
				}
				else
				{
					ViewModel.ProfileManager.LoadProfile(p_impProfile, out dicProfile);
					if ((dicProfile != null) && (dicProfile.Count > 0) && dicProfile.ContainsKey("modlist"))
					{
						lstVirtualLinks = ViewModel.ModManager.VirtualModActivator.LoadImportedList(dicProfile["modlist"]);
						dicNotFound = ViewModel.ModManager.VirtualModActivator.CheckLinkListIntegrity(lstVirtualLinks);
					}
				}

				if ((dicNotFound != null) && (dicNotFound.Count > 0))
				{
					System.Text.StringBuilder sbMessage = new System.Text.StringBuilder();
					string strDetails;

					sbMessage.Append("The selected profile contains files from mods not currently installed.");
					sbMessage.AppendLine("Do you want the manager to try and install the files present in the folder that aren't currently installed?");
					System.Text.StringBuilder sbDetails = new System.Text.StringBuilder();

					List<IMod> lstFoundMods = new List<IMod>();
					List<IMod> lstManagedMods = new List<IMod>(ViewModel.ModManager.ManagedMods.ToList());
					foreach (KeyValuePair<string, string> kvp in dicNotFound)
					{
						IMod modMod = lstManagedMods.Find(x => x.Filename == kvp.Key);
						if (modMod != null)
							lstFoundMods.Add(modMod);
						sbDetails.AppendFormat("- Mod: {0} - filename: {1} - present: {2}", kvp.Value, kvp.Key, (modMod != null) ? "Yes" : "No").AppendLine();
					}
					if (sbDetails.Length > 0)
						strDetails = sbDetails.ToString();
					else
						strDetails = null;

					m_booIsSwitching = true;
					ViewModel.ModManager.VirtualModActivator.DisableLinkCreation = true;

					if (p_booSilentInstall)
						mmgModManager.MultiModInstall(lstFoundMods, false);
					else
					{
						DialogResult drResult = ExtendedMessageBox.Show(this, sbMessage.ToString(), ViewModel.ModManagerVM.Settings.ModManagerName, strDetails, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
						if (drResult == DialogResult.Yes)
						{
							if (lstFoundMods.Count > 0)
								mmgModManager.MultiModInstall(lstFoundMods, false);
						}
						else if (drResult == DialogResult.Cancel)
							return;
					}
				}

				if (ViewModel.GameMode.RequiresOptionalFilesCheckOnProfileSwitch)
					if ((dicProfile != null) && (dicProfile.Count > 0) && dicProfile.ContainsKey("optional"))
					{
						string[] strFiles = dicProfile["optional"].Split("#".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
						if ((strFiles != null) && (strFiles.Length > 0))
							ViewModel.GameMode.SetOptionalFilesList(strFiles);
						if (ViewModel.PluginManager != null)
							foreach (string strFile in strFiles)
							{
								if (ViewModel.PluginManager.IsActivatiblePluginFile(strFile))
									ViewModel.PluginManager.AddPlugin(strFile);
							}
					}

				ViewModel.ModManager.VirtualModActivator.DisableLinkCreation = false;
				ViewModel.ProfileSwitch(p_impProfile, lstVirtualLinks);
			}
		}

		/// <summary>
		/// Confirms if the manager should close after launching the game.
		/// </summary>
		/// <param name="p_booRememberSelection">Whether the selected response should be remembered.</param>
		/// <returns><c>true</c> if the manager should close after game launch;
		/// <c>false</c> otherwise.</returns>
		private bool ConfirmCloseAfterGameLaunch(out bool p_booRememberSelection)
		{
			bool booRemember = false;
			bool booClose = (ExtendedMessageBox.Show(this, String.Format("Would you like {0} to close after launching the game?", ViewModel.EnvironmentInfo.Settings.ModManagerName), "Close", "Details", MessageBoxButtons.YesNo, MessageBoxIcon.Question, out booRemember) == DialogResult.Yes);
			p_booRememberSelection = booRemember;
			return booClose;
		}

		/// <summary>
		/// Handles the <see cref="IGameLauncher.GameLaunched"/> event of the game launcher.
		/// </summary>
		/// <remarks>This displays any message resulting from the game launch. If the launch was successful, the
		/// form is closed.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="GameLaunchEventArgs"/> describing the event arguments.</param>
		private void GameLauncher_GameLaunched(object sender, GameLaunchEventArgs e)
		{
			if (!e.Launched)
				MessageBox.Show(this, e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			else if (ViewModel.EnvironmentInfo.Settings.CloseModManagerAfterGameLaunch)
				Close();
		}

		#endregion

		/// <summary>
		/// Applies the given theme to the form.
		/// </summary>
		/// <param name="p_thmTheme">The theme to apply.</param>
		protected void ApplyTheme(Theme p_thmTheme)
		{
			Icon = p_thmTheme.Icon;

			Bitmap imgChangeMod = new Bitmap(spbChangeMode.Image);
			Color clrOld = Color.Fuchsia;
			for (Int32 y = 0; y < imgChangeMod.Height; y++)
			{
				for (Int32 x = 0; x < imgChangeMod.Width; x++)
				{
					clrOld = imgChangeMod.GetPixel(x, y);

					byte r = clrOld.R;
					byte g = clrOld.G;
					byte b = clrOld.B;

					r = g = b = (byte)(0.21 * r + 0.72 * g + 0.07 * b);

					r = (byte)(r / 255.0 * p_thmTheme.PrimaryColour.R);
					g = (byte)(g / 255.0 * p_thmTheme.PrimaryColour.G);
					b = (byte)(b / 255.0 * p_thmTheme.PrimaryColour.B);

					imgChangeMod.SetPixel(x, y, Color.FromArgb(clrOld.A, (Int32)r, (Int32)g, (Int32)b));
				}
			}
			spbChangeMode.Image = imgChangeMod;
		}

		#region Form Events

		/// <summary>
		/// Raises the <see cref="Form.Closing"/> event.
		/// </summary>
		/// <remarks>
		/// This saves the current window position.
		/// </remarks>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		protected override void OnClosing(CancelEventArgs e)
		{
			base.OnClosing(e);
			ViewModel.EnvironmentInfo.Settings.WindowPositions.SetWindowPosition("MainForm", this);
		}

		/// <summary>
		/// Raises the <see cref="Control.Resize"/> event.
		/// </summary>
		/// <remarks>
		/// This saves the last window state before the form was minimized.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			if (WindowState != FormWindowState.Minimized)
				m_fwsLastWindowState = WindowState;
			else if ((bmBalloon != null) && (bmBalloon.balloonHelp != null) && (bmBalloon.balloonHelp.Visible))
				bmBalloon.balloonHelp.Close();
		}

		/// <summary>
		/// Raises the <see cref="Form.Shown"/> event.
		/// </summary>
		/// <remarks>
		/// This notifies the view model the view is visible.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			ShowStartupMessage();
			ViewModel.ViewIsShown();
			LoadTips();
		}

		#endregion

		/// <summary>
		/// Shows a startup message if needed.
		/// </summary>
		private void ShowStartupMessage()
		{
			if (ViewModel.EnvironmentInfo.Settings.ShowStartupMessage)
			{
				StringBuilder stbWarning = new StringBuilder();
				stbWarning.AppendLine("Recently some spam emails have been doing the rounds about a new version of the Nexus Mod Manager, telling you to upgrade to the latest version.");
				stbWarning.AppendLine("We have never emailed anyone in regards to NMM (indeed, the last time we did a mass email was in 2007), and if we did,");
				stbWarning.AppendLine("we would never send it as an attachment or send you a link that would go anywhere other than the nexusmods.com domain.").AppendLine();
				stbWarning.AppendLine("Please remain vigilant while you browse the Nexus and indeed the internet in general. The only place you should download NMM from is the nexusmods.com website or through NMM's built in updater.").AppendLine();
				stbWarning.AppendLine("For more details on this topic please see the latest site news updates.");
				MessageBox.Show(stbWarning.ToString(), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);

				ViewModel.EnvironmentInfo.Settings.ShowStartupMessage = false;
				ViewModel.EnvironmentInfo.Settings.Save();
			}
		}

		/// <summary>
		/// Restores focus to the form.
		/// </summary>
		public void RestoreFocus()
		{
			WindowState = m_fwsLastWindowState;
			Activate();
		}
	}
}
