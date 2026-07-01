using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DiskAccessLibrary;

namespace ISCSIConsole
{
    public partial class CreateDiskImageForm : Form
    {
        private DiskImage m_diskImage;
        private bool m_isWorking = false;

        public CreateDiskImageForm()
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
            long size = (long)numericDiskSize.Value * 1024 * 1024;
            if (path == String.Empty)
            {
                MessageBox.Show("请选择文件位置", "错误");
                return;
            }
            m_isWorking = true;
            new Thread(delegate()
            {
                DiskImage diskImage;
                try
                {
#if !NET20
                    if (path.EndsWith(".vhdx", StringComparison.InvariantCultureIgnoreCase))
                    {
                        diskImage = VhdxDiskImage.CreateDynamicDisk(path, size);
                    }
                    else
#endif
                    {
                        diskImage = VirtualHardDisk.CreateFixedDisk(path, size);
                    }
                }
                catch (IOException ex)
                {
                    this.Invoke((MethodInvoker)delegate()
                    {
                        MessageBox.Show("创建磁盘失败: " + ex.Message, "错误");
                        txtFilePath.Enabled = true;
                        btnBrowse.Enabled = true;
                        numericDiskSize.Enabled = true;
                        btnOK.Enabled = true;
                        btnCancel.Enabled = true;
                    });
                    m_isWorking = false;
                    return;
                }
                bool isLocked = diskImage.ExclusiveLock();
                if (!isLocked)
                {
                    this.Invoke((MethodInvoker)delegate()
                    {
                        MessageBox.Show("无法以独占方式锁定磁盘镜像", "错误");
                        txtFilePath.Enabled = true;
                        btnBrowse.Enabled = true;
                        numericDiskSize.Enabled = true;
                        btnOK.Enabled = true;
                        btnCancel.Enabled = true;
                    });
                    m_isWorking = false;
                    return;
                }
                m_diskImage = diskImage;
                m_isWorking = false;
                this.Invoke((MethodInvoker)delegate()
                {
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                });
            }).Start();
            txtFilePath.Enabled = false;
            btnBrowse.Enabled = false;
            numericDiskSize.Enabled = false;
            btnOK.Enabled = false;
            btnCancel.Enabled = false;
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            DialogResult result = saveVirtualDiskFileDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                txtFilePath.Text = saveVirtualDiskFileDialog.FileName;
            }
        }

        public DiskImage DiskImage
        {
            get
            {
                return m_diskImage;
            }
        }

        private void CreateDiskImageForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (m_isWorking)
            {
                e.Cancel = true;
                MessageBox.Show("请等待磁盘镜像创建完成。", "错误");
            }
        }
    }
}
