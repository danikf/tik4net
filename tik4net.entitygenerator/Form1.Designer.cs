namespace tik4net.entitygenerator
{
    partial class Form1
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
            System.Windows.Forms.Label label1;
            this.btnGenerate = new System.Windows.Forms.Button();
            this.tbPath = new System.Windows.Forms.TextBox();
            this.tbSourceCode = new System.Windows.Forms.TextBox();
            this.cbIncludeDetails = new System.Windows.Forms.CheckBox();
            label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(12, 15);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(57, 13);
            label1.TabIndex = 3;
            label1.Text = "Entity path";
            // 
            // btnGenerate
            // 
            this.btnGenerate.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnGenerate.Location = new System.Drawing.Point(619, 343);
            this.btnGenerate.Name = "btnGenerate";
            this.btnGenerate.Size = new System.Drawing.Size(130, 23);
            this.btnGenerate.TabIndex = 0;
            this.btnGenerate.Text = "Load and generate";
            this.btnGenerate.UseVisualStyleBackColor = true;
            this.btnGenerate.Click += new System.EventHandler(this.btnGenerate_Click);
            // 
            // tbPath
            // 
            this.tbPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tbPath.Location = new System.Drawing.Point(75, 12);
            this.tbPath.Name = "tbPath";
            this.tbPath.Size = new System.Drawing.Size(674, 20);
            this.tbPath.TabIndex = 1;
            this.tbPath.Text = "/";
            // 
            // tbSourceCode
            // 
            this.tbSourceCode.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tbSourceCode.Location = new System.Drawing.Point(12, 63);
            this.tbSourceCode.Multiline = true;
            this.tbSourceCode.Name = "tbSourceCode";
            this.tbSourceCode.Size = new System.Drawing.Size(737, 274);
            this.tbSourceCode.TabIndex = 2;
            // 
            // cbIncludeDetails
            // 
            this.cbIncludeDetails.AutoSize = true;
            this.cbIncludeDetails.Checked = true;
            this.cbIncludeDetails.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbIncludeDetails.Location = new System.Drawing.Point(15, 41);
            this.cbIncludeDetails.Name = "cbIncludeDetails";
            this.cbIncludeDetails.Size = new System.Drawing.Size(94, 17);
            this.cbIncludeDetails.TabIndex = 4;
            this.cbIncludeDetails.Text = "Include details";
            this.cbIncludeDetails.UseVisualStyleBackColor = true;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(761, 378);
            this.Controls.Add(this.cbIncludeDetails);
            this.Controls.Add(label1);
            this.Controls.Add(this.tbSourceCode);
            this.Controls.Add(this.tbPath);
            this.Controls.Add(this.btnGenerate);
            this.Name = "Form1";
            this.Text = "Entity source code generator";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnGenerate;
        private System.Windows.Forms.TextBox tbPath;
        private System.Windows.Forms.TextBox tbSourceCode;
        private System.Windows.Forms.CheckBox cbIncludeDetails;
    }
}

