using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DiskAccessLibrary;

namespace ISCSIConsole
{
    public partial class SelectDiskImageForm : Form
    {
        private DiskImage m_diskImage;
        private int m_cacheSizeMB = CachedDisk.DefaultCacheSizeMB;

        public SelectDiskImageForm()
        {
            InitializeComponent();
            numericCacheSize.Value = CachedDisk.DefaultCacheSizeMB;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            string path = txtFilePath.Text;
            if (path == String.Empty)
            {
                MessageBox.Show("请选择文件位置。", "错误");
                return;
            }
            DiskImage diskImage;
            bool usedReadOnlyFallback = false;
            try
            {
                diskImage = OpenDiskImage(path, chkReadOnly.Checked, out usedReadOnlyFallback);
            }
            catch (IOException ex)
            {
                MessageBox.Show("无法打开磁盘镜像: " + ex.Message, "错误");
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show("没有权限打开磁盘镜像: " + ex.Message, "错误");
                return;
            }
            catch (InvalidDataException ex)
            {
                MessageBox.Show("磁盘镜像无效: " + ex.Message, "错误");
                return;
            }
            catch (NotImplementedException)
            {
                MessageBox.Show("不支持的磁盘镜像格式", "错误");
                return;
            }

            bool isLocked = false;
            try
            {
                isLocked = diskImage.ExclusiveLock();
            }
            catch (IOException)
            {
            }
            if (!isLocked)
            {
                if (diskImage.IsReadOnly)
                {
                    DialogResult continueReadOnly = MessageBox.Show(
                        "无法以独占方式锁定磁盘镜像。\r\n\r\n磁盘已只读打开，是否继续以只读方式添加？",
                        "提示",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (continueReadOnly != DialogResult.Yes)
                    {
                        diskImage.ReleaseLock();
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("无法以独占方式锁定磁盘镜像。", "错误");
                    diskImage.ReleaseLock();
                    return;
                }
            }
            if (usedReadOnlyFallback)
            {
                MessageBox.Show("磁盘镜像无法以读写方式打开，已自动改为只读方式。", "提示");
            }
            m_cacheSizeMB = (int)numericCacheSize.Value;
            m_diskImage = diskImage;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private static DiskImage OpenDiskImage(string path, bool readOnly, out bool usedReadOnlyFallback)
        {
            usedReadOnlyFallback = false;
            try
            {
                return OpenDiskImage(path, readOnly);
            }
            catch (IOException)
            {
                if (readOnly)
                {
                    throw;
                }
            }
            catch (UnauthorizedAccessException)
            {
                if (readOnly)
                {
                    throw;
                }
            }

            usedReadOnlyFallback = true;
            return OpenDiskImage(path, true);
        }

        private static DiskImage OpenDiskImage(string path, bool readOnly)
        {
#if !NET20
            if (path.EndsWith(".vhdx", StringComparison.InvariantCultureIgnoreCase))
            {
                return new VhdxDiskImage(path, readOnly);
            }

            if (path.EndsWith(".vhd", StringComparison.InvariantCultureIgnoreCase))
            {
                try
                {
                    return DiskImage.GetDiskImage(path, readOnly);
                }
                catch (NotImplementedException)
                {
                    return new VhdDiskImage(path, readOnly);
                }
            }
#endif
            return DiskImage.GetDiskImage(path, readOnly);
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            DialogResult result = openDiskImageDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtFilePath.Text = openDiskImageDialog.FileName;
            }
        }

        public DiskImage DiskImage
        {
            get
            {
                return m_diskImage;
            }
        }

        public int CacheSizeMB
        {
            get
            {
                return m_cacheSizeMB;
            }
        }
    }
}
