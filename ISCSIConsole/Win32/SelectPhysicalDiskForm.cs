using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using DiskAccessLibrary;
using DiskAccessLibrary.LogicalDiskManager;
using DiskAccessLibrary.Win32;
using Utilities;

namespace ISCSIConsole
{
    public partial class SelectPhysicalDiskForm : Form
    {
        private PhysicalDisk m_selectedDisk;

        public SelectPhysicalDiskForm()
        {
            InitializeComponent();
        }

        private void SelectPhysicalDiskForm_Load(object sender, EventArgs e)
        {
            List<PhysicalDisk> physicalDisks = PhysicalDiskHelper.GetPhysicalDisks();
            if (Environment.OSVersion.Version.Major >= 6)
            {
                listPhysicalDisks.Columns.Add("状态", 60);
                columnDescription.Width -= 60;
            }
            foreach (PhysicalDisk physicalDisk in physicalDisks)
            {
                string title = String.Format("磁盘 {0}", physicalDisk.PhysicalDiskIndex);
                string description = physicalDisk.Description;
                string serialNumber = physicalDisk.SerialNumber;
                string sizeString = FormattingHelper.GetStandardSizeString(physicalDisk.Size);
                ListViewItem item = new ListViewItem(title);
                item.SubItems.Add(description);
                item.SubItems.Add(serialNumber);
                item.SubItems.Add(sizeString);
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    bool? isOnline = null;
                    try
                    {
                        isOnline = physicalDisk.GetOnlineStatus();
                    }
                    catch (Exception)
                    {
                    }
                    string status = isOnline.HasValue ? (isOnline.Value ? "联机" : "脱机") : "未知";
                    item.SubItems.Add(status);
                }
                item.Tag = physicalDisk;
                listPhysicalDisks.Items.Add(item);
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            PhysicalDisk selectedDisk;
            if (listPhysicalDisks.SelectedItems.Count > 0)
            {
                selectedDisk = (PhysicalDisk)listPhysicalDisks.SelectedItems[0].Tag;
            }
            else
            {
                MessageBox.Show("未选择磁盘", "错误");
                return;
            }
            if (!chkReadOnly.Checked)
            {
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    bool isDiskReadOnly;
                    bool isOnline;
                    try
                    {
                        isOnline = selectedDisk.GetOnlineStatus(out isDiskReadOnly);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "错误");
                        return;
                    }

                    if (isDiskReadOnly)
                    {
                        MessageBox.Show("所选磁盘已设置为只读", "错误");
                        return;
                    }

                    if (isOnline)
                    {
                        DialogResult result = MessageBox.Show("所选磁盘将被设置为脱机。是否继续?", String.Empty, MessageBoxButtons.OKCancel);
                        if (result == DialogResult.OK)
                        {
                            bool success = selectedDisk.SetOnlineStatus(false);
                            if (!success)
                            {
                                MessageBox.Show("无法将磁盘设置为脱机", "错误");
                                return;
                            }
                        }
                        else
                        {
                            return;
                        }
                    }
                }
                else
                {
                    if (DynamicDisk.IsDynamicDisk(selectedDisk))
                    {
                        // The user will probably want to stop the Logical Disk Manager services (vds, dmadmin, dmserver)
                        // and lock all dynamic disks and dynamic volumes before whatever he's doing.
                        // Modifications the the LDM database should be applied to all dynamic disks.
                        DialogResult result = MessageBox.Show("动态磁盘数据库可能会损坏。是否继续?", "警告", MessageBoxButtons.YesNo);
                        if (result != DialogResult.Yes)
                        {
                            return;
                        }
                    }
                    else
                    {
                        // Locking a disk does not prevent Windows from accessing mounted volumes on it. (it does prevent creation of new volumes).
                        // For basic disks we need to lock the Disk and Volumes, and we should also call UpdateDiskProperties() after releasing the lock.
                        LockStatus status = LockHelper.LockBasicDiskAndVolumesOrNone(selectedDisk);
                        if (status == LockStatus.CannotLockDisk)
                        {
                            MessageBox.Show("无法锁定磁盘", "错误");
                            return;
                        }
                        else if (status == LockStatus.CannotLockVolume)
                        {
                            MessageBox.Show("无法锁定磁盘上的某个卷", "错误");
                            return;
                        }
                    }
                }
            }
            if (chkReadOnly.Checked)
            {
                selectedDisk = new PhysicalDisk(selectedDisk.PhysicalDiskIndex, true);
            }
            m_selectedDisk = selectedDisk;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        public PhysicalDisk SelectedDisk
        {
            get
            {
                return m_selectedDisk;
            }
        }

        private void listPhysicalDisks_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            e.NewWidth = ((ListView)sender).Columns[e.ColumnIndex].Width;
            e.Cancel = true;
        }
    }
}
