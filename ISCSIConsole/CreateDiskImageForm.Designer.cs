namespace ISCSIConsole
{
    partial class CreateDiskImageForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CreateDiskImageForm));
            this.saveVirtualDiskFileDialog = new System.Windows.Forms.SaveFileDialog();
            this.numericDiskSize = new System.Windows.Forms.NumericUpDown();
            this.lblSize = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.lblFilePath = new System.Windows.Forms.Label();
            this.txtFilePath = new System.Windows.Forms.TextBox();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.lblCacheSize = new System.Windows.Forms.Label();
            this.numericCacheSize = new System.Windows.Forms.NumericUpDown();
            this.lblCacheMB = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numericDiskSize)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericCacheSize)).BeginInit();
            this.SuspendLayout();
            //
            // saveVirtualDiskFileDialog
            //
            this.saveVirtualDiskFileDialog.FileName = "Disk.vhdx";
            this.saveVirtualDiskFileDialog.Filter = "虚拟硬盘 (*.vhd,*.vhdx)|*.vhd;*.vhdx|VHD 文件 (*.vhd)|*.vhd|VHDX 文件 (*.vhdx)|*.vhdx";
            //
            // numericDiskSize
            //
            this.numericDiskSize.Location = new System.Drawing.Point(56, 41);
            this.numericDiskSize.Maximum = new decimal(new int[] {
            16777215,
            0,
            0,
            0});
            this.numericDiskSize.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericDiskSize.Name = "numericDiskSize";
            this.numericDiskSize.Size = new System.Drawing.Size(86, 20);
            this.numericDiskSize.TabIndex = 4;
            this.numericDiskSize.ThousandsSeparator = true;
            this.numericDiskSize.Value = new decimal(new int[] {
            1024,
            0,
            0,
            0});
            //
            // lblSize
            //
            this.lblSize.AutoSize = true;
            this.lblSize.Location = new System.Drawing.Point(12, 43);
            this.lblSize.Name = "lblSize";
            this.lblSize.Size = new System.Drawing.Size(30, 13);
            this.lblSize.TabIndex = 3;
            this.lblSize.Text = "大小:";
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(148, 43);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(23, 13);
            this.label1.TabIndex = 5;
            this.label1.Text = "MB";
            //
            // lblFilePath
            //
            this.lblFilePath.AutoSize = true;
            this.lblFilePath.Location = new System.Drawing.Point(12, 18);
            this.lblFilePath.Name = "lblFilePath";
            this.lblFilePath.Size = new System.Drawing.Size(26, 13);
            this.lblFilePath.TabIndex = 0;
            this.lblFilePath.Text = "文件:";
            //
            // txtFilePath
            //
            this.txtFilePath.Location = new System.Drawing.Point(56, 15);
            this.txtFilePath.Name = "txtFilePath";
            this.txtFilePath.Size = new System.Drawing.Size(243, 20);
            this.txtFilePath.TabIndex = 1;
            //
            // btnBrowse
            //
            this.btnBrowse.Location = new System.Drawing.Point(305, 13);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(75, 23);
            this.btnBrowse.TabIndex = 2;
            this.btnBrowse.Text = "浏览...";
            this.btnBrowse.UseVisualStyleBackColor = true;
            this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);
            //
            // btnOK
            //
            this.btnOK.Location = new System.Drawing.Point(224, 118);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(75, 23);
            this.btnOK.TabIndex = 9;
            this.btnOK.Text = "确定";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            //
            // btnCancel
            //
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(305, 118);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 10;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // lblCacheSize
            //
            this.lblCacheSize.AutoSize = true;
            this.lblCacheSize.Location = new System.Drawing.Point(12, 72);
            this.lblCacheSize.Name = "lblCacheSize";
            this.lblCacheSize.Size = new System.Drawing.Size(46, 13);
            this.lblCacheSize.TabIndex = 6;
            this.lblCacheSize.Text = "读缓存:";
            //
            // numericCacheSize
            //
            this.numericCacheSize.Location = new System.Drawing.Point(56, 69);
            this.numericCacheSize.Maximum = new decimal(new int[] {
            1048576,
            0,
            0,
            0});
            this.numericCacheSize.Name = "numericCacheSize";
            this.numericCacheSize.Size = new System.Drawing.Size(86, 20);
            this.numericCacheSize.TabIndex = 7;
            this.numericCacheSize.ThousandsSeparator = true;
            this.numericCacheSize.Value = new decimal(new int[] {
            256,
            0,
            0,
            0});
            //
            // lblCacheMB
            //
            this.lblCacheMB.AutoSize = true;
            this.lblCacheMB.Location = new System.Drawing.Point(148, 72);
            this.lblCacheMB.Name = "lblCacheMB";
            this.lblCacheMB.Size = new System.Drawing.Size(23, 13);
            this.lblCacheMB.TabIndex = 8;
            this.lblCacheMB.Text = "MB";
            //
            // CreateDiskImageForm
            //
            this.AcceptButton = this.btnOK;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(394, 155);
            this.Controls.Add(this.lblCacheMB);
            this.Controls.Add(this.numericCacheSize);
            this.Controls.Add(this.lblCacheSize);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.btnBrowse);
            this.Controls.Add(this.txtFilePath);
            this.Controls.Add(this.lblFilePath);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.lblSize);
            this.Controls.Add(this.numericDiskSize);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MaximumSize = new System.Drawing.Size(420, 200);
            this.MinimumSize = new System.Drawing.Size(420, 200);
            this.Name = "CreateDiskImageForm";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Text = "创建虚拟磁盘";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.CreateDiskImageForm_FormClosing);
            ((System.ComponentModel.ISupportInitialize)(this.numericDiskSize)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericCacheSize)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.SaveFileDialog saveVirtualDiskFileDialog;
        private System.Windows.Forms.NumericUpDown numericDiskSize;
        private System.Windows.Forms.Label lblSize;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label lblFilePath;
        private System.Windows.Forms.TextBox txtFilePath;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Label lblCacheSize;
        private System.Windows.Forms.NumericUpDown numericCacheSize;
        private System.Windows.Forms.Label lblCacheMB;
    }
}
