using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DiskAccessLibrary;
using DiskAccessLibrary.Win32;
using ISCSI.Server;
using Utilities;

namespace ISCSIConsole
{
    public partial class AddTargetForm : Form
    {
        public static int m_targetNumber = 1;
        public const string DefaultTargetIQN = "iqn.1991-05.com.microsoft";

        private List<Disk> m_disks = new List<Disk>();
        private List<DiskConfiguration> m_diskConfigurations = new List<DiskConfiguration>();
        private ISCSITarget m_target;
        private TargetConfiguration m_targetConfiguration;

        public AddTargetForm()
        {
            InitializeComponent();
            if (RuntimeHelper.IsWin32)
            {
                btnAddPhysicalDisk.Visible = true;
                btnAddVolume.Visible = true;
                if (!SecurityHelper.IsAdministrator())
                {
                    btnAddPhysicalDisk.Enabled = false;
                    btnAddVolume.Enabled = false;
                }
            }
        }

        private void AddTargetForm_Load(object sender, EventArgs e)
        {
            txtTargetIQN.Text = String.Format("{0}:target{1}", DefaultTargetIQN, m_targetNumber);
        }

        private void btnCreateDiskImage_Click(object sender, EventArgs e)
        {
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                CreateRAMDiskForm createRAMDisk = new CreateRAMDiskForm();
                DialogResult result = createRAMDisk.ShowDialog();
                if (result == DialogResult.OK)
                {
                    RAMDisk ramDisk = createRAMDisk.RAMDisk;
                    AddDisk(ramDisk, null);
                }
            }
            else
            {
                CreateDiskImageForm createDiskImage = new CreateDiskImageForm();
                DialogResult result = createDiskImage.ShowDialog();
                if (result == DialogResult.OK)
                {
                    DiskImage diskImage = createDiskImage.DiskImage;
                    AddDisk(diskImage, DiskConfiguration.CreateDiskImage(diskImage.Path, diskImage.IsReadOnly));
                }
            }
        }

        private void btnAddDiskImage_Click(object sender, EventArgs e)
        {
            SelectDiskImageForm selectDiskImage = new SelectDiskImageForm();
            DialogResult result = selectDiskImage.ShowDialog();
            if (result == DialogResult.OK)
            {
                DiskImage diskImage = selectDiskImage.DiskImage;
                AddDisk(diskImage, DiskConfiguration.CreateDiskImage(diskImage.Path, diskImage.IsReadOnly));
            }
        }

        private void btnAddPhysicalDisk_Click(object sender, EventArgs e)
        {
            SelectPhysicalDiskForm selectPhysicalDisk = new SelectPhysicalDiskForm();
            DialogResult result = selectPhysicalDisk.ShowDialog();
            if (result == DialogResult.OK)
            {
                PhysicalDisk selectedDisk = selectPhysicalDisk.SelectedDisk;
                AddDisk(selectedDisk, DiskConfiguration.CreatePhysicalDisk(selectedDisk.PhysicalDiskIndex, selectedDisk.IsReadOnly));
            }
        }

        private void btnAddVolume_Click(object sender, EventArgs e)
        {
            SelectVolumeForm selectVolume = new SelectVolumeForm();
            DialogResult result = selectVolume.ShowDialog();
            if (result == DialogResult.OK)
            {
                Guid? volumeGuid = selectVolume.SelectedVolumeGuid;
                if (!volumeGuid.HasValue)
                {
                    MessageBox.Show("所选卷没有可保存的 Windows 卷 GUID，无法写入服务配置。", "错误");
                    return;
                }
                VolumeDisk volumeDisk = new VolumeDisk(selectVolume.SelectedVolume, selectVolume.IsReadOnly);
                AddDisk(volumeDisk, DiskConfiguration.CreateVolume(volumeGuid.Value, selectVolume.IsReadOnly));
            }
        }

        private void AddDisk(Disk disk, DiskConfiguration diskConfiguration)
        {
            string description = String.Empty;
            string sizeString = FormattingHelper.GetStandardSizeString(disk.Size);
            if (disk is DiskImage)
            {
                description = ((DiskImage)disk).Path;
            }
            else if (disk is RAMDisk)
            {
                description = "RAM 磁盘";
            }
            else if (disk is PhysicalDisk) // Win32 only
            {
                description = String.Format("物理磁盘 {0}", ((PhysicalDisk)disk).PhysicalDiskIndex);
            }
            else if (disk is VolumeDisk) // Win32 only
            {
                description = String.Format("卷");
            }

            ListViewItem item = new ListViewItem(description);
            item.SubItems.Add(sizeString);
            listDisks.Items.Add(item);
            m_disks.Add(disk);
            m_diskConfigurations.Add(diskConfiguration);
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            if (listDisks.SelectedIndices.Count > 0)
            {
                int selectedIndex = listDisks.SelectedIndices[0];
                LockUtils.ReleaseDisk(m_disks[selectedIndex]);
                m_disks.RemoveAt(selectedIndex);
                m_diskConfigurations.RemoveAt(selectedIndex);
                listDisks.Items.RemoveAt(selectedIndex);
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (m_disks.Count == 0)
            {
                MessageBox.Show("请先添加至少一个磁盘。", "错误");
                return;
            }
            if (!ISCSINameHelper.IsValidIQN(txtTargetIQN.Text))
            {
                MessageBox.Show("目标 IQN 无效", "错误");
                return;
            }
            m_target = new ISCSITarget(txtTargetIQN.Text, m_disks);
            m_targetConfiguration = BuildTargetConfiguration(txtTargetIQN.Text);
            m_targetNumber++;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private TargetConfiguration BuildTargetConfiguration(string targetName)
        {
            TargetConfiguration configuration = new TargetConfiguration();
            configuration.TargetName = targetName;
            foreach (DiskConfiguration diskConfiguration in m_diskConfigurations)
            {
                if (diskConfiguration == null)
                {
                    return null;
                }
                configuration.Disks.Add(diskConfiguration);
            }
            return configuration;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        public ISCSITarget Target
        {
            get
            {
                return m_target;
            }
        }

        public TargetConfiguration TargetConfiguration
        {
            get
            {
                return m_targetConfiguration;
            }
        }

        private void listDisks_SelectedIndexChanged(object sender, EventArgs e)
        {
            btnRemove.Enabled = (listDisks.SelectedIndices.Count > 0);
        }

        private void listDisks_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            e.NewWidth = ((ListView)sender).Columns[e.ColumnIndex].Width;
            e.Cancel = true;
        }

        private void AddTargetForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.DialogResult != DialogResult.OK)
            {
                LockUtils.ReleaseDisks(m_disks);
            }
        }

        private void AddTargetForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                btnCreateDiskImage.Text = "创建 RAM 磁盘";
            }
        }

        private void AddTargetForm_KeyUp(object sender, KeyEventArgs e)
        {
            btnCreateDiskImage.Text = "创建虚拟磁盘";
        }

        private void AddTargetForm_Deactivate(object sender, EventArgs e)
        {
            btnCreateDiskImage.Text = "创建虚拟磁盘";
        }
    }
}
