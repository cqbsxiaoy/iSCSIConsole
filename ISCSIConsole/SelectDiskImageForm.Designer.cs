namespace ISCSIConsole
{
    partial class SelectDiskImageForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SelectDiskImageForm));
            this.lblFilePath = new System.Windows.Forms.Label();
            this.txtFilePath = new System.Windows.Forms.TextBox();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.chkReadOnly = new System.Windows.Forms.CheckBox();
            this.openDiskImageDialog = new System.Windows.Forms.OpenFileDialog();
            this.lblCacheSize = new System.Windows.Forms.Label();
            this.numericCacheSize = new System.Windows.Forms.NumericUpDown();
            this.lblCacheMB = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numericCacheSize)).BeginInit();
            this.SuspendLayout();
            //
            // lblFilePath
            //
            this.lblFilePath.AutoSize = true;
            this.lblFilePath.Location = new System.Drawing.Point(12, 18);
            this.lblFilePath.Name = "lblFilePath";
            this.lblFilePath.Size = new System.Drawing.Size(26, 13);
            this.lblFilePath.TabIndex = 3;
            this.lblFilePath.Text = "文件:";
            //
            // txtFilePath
            //
            this.txtFilePath.Location = new System.Drawing.Point(56, 15);
            this.txtFilePath.Name = "txtFilePath";
            this.txtFilePath.Size = new System.Drawing.Size(243, 20);
            this.txtFilePath.TabIndex = 4;
            //
            // btnBrowse
            //
            this.btnBrowse.Location = new System.Drawing.Point(305, 13);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(75, 23);
            this.btnBrowse.TabIndex = 5;
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
            // chkReadOnly
            //
            this.chkReadOnly.AutoSize = true;
            this.chkReadOnly.Location = new System.Drawing.Point(12, 45);
            this.chkReadOnly.Name = "chkReadOnly";
            this.chkReadOnly.Size = new System.Drawing.Size(74, 17);
            this.chkReadOnly.TabIndex = 6;
            this.chkReadOnly.Text = "只读";
            this.chkReadOnly.UseVisualStyleBackColor = true;
            //
            // openDiskImageDialog
            //
            this.openDiskImageDialog.Filter = resources.GetString("openDiskImageDialog.Filter");
            //
            // lblCacheSize
            //
            this.lblCacheSize.AutoSize = true;
            this.lblCacheSize.Location = new System.Drawing.Point(12, 74);
            this.lblCacheSize.Name = "lblCacheSize";
            this.lblCacheSize.Size = new System.Drawing.Size(46, 13);
            this.lblCacheSize.TabIndex = 7;
            this.lblCacheSize.Text = "读缓存:";
            //
            // numericCacheSize
            //
            this.numericCacheSize.Location = new System.Drawing.Point(70, 71);
            this.numericCacheSize.Maximum = new decimal(new int[] {
            1048576,
            0,
            0,
            0});
            this.numericCacheSize.Name = "numericCacheSize";
            this.numericCacheSize.Size = new System.Drawing.Size(86, 20);
            this.numericCacheSize.TabIndex = 8;
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
            this.lblCacheMB.Location = new System.Drawing.Point(162, 74);
            this.lblCacheMB.Name = "lblCacheMB";
            this.lblCacheMB.Size = new System.Drawing.Size(23, 13);
            this.lblCacheMB.TabIndex = 11;
            this.lblCacheMB.Text = "MB";
            //
            // SelectDiskImageForm
            //
            this.AcceptButton = this.btnOK;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(394, 155);
            this.Controls.Add(this.lblCacheMB);
            this.Controls.Add(this.numericCacheSize);
            this.Controls.Add(this.lblCacheSize);
            this.Controls.Add(this.chkReadOnly);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.btnBrowse);
            this.Controls.Add(this.txtFilePath);
            this.Controls.Add(this.lblFilePath);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MaximumSize = new System.Drawing.Size(420, 200);
            this.MinimumSize = new System.Drawing.Size(420, 200);
            this.Name = "SelectDiskImageForm";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Text = "选择磁盘镜像";
            ((System.ComponentModel.ISupportInitialize)(this.numericCacheSize)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblFilePath;
        private System.Windows.Forms.TextBox txtFilePath;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.CheckBox chkReadOnly;
        private System.Windows.Forms.OpenFileDialog openDiskImageDialog;
        private System.Windows.Forms.Label lblCacheSize;
        private System.Windows.Forms.NumericUpDown numericCacheSize;
        private System.Windows.Forms.Label lblCacheMB;
    }
}
