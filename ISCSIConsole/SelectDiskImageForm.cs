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

        public SelectDiskImageForm()
        {
            InitializeComponent();
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
            try
            {
#if !NET20
                if (path.EndsWith(".vhdx", StringComparison.InvariantCultureIgnoreCase))
                {
                    diskImage = new VhdxDiskImage(path, chkReadOnly.Checked);
                }
                else
#endif
                {
                    diskImage = DiskImage.GetDiskImage(path, chkReadOnly.Checked);
                }
            }
            catch (IOException ex)
            {
                MessageBox.Show("无法打开磁盘镜像: " + ex.Message, "错误");
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
                MessageBox.Show("无法以独占方式锁定磁盘镜像。", "错误");
                return;
            }
            m_diskImage = diskImage;
            this.DialogResult = DialogResult.OK;
            this.Close();
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
    }
}
