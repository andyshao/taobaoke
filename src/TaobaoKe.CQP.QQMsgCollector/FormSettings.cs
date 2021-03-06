﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using TaobaoKe.Core.IPC;
using TaobaoKe.Forms;

namespace Rap.CQP.QQMsgCollector
{
    public partial class FormSettings : Form
    {
        public FormSettings()
        {
            InitializeComponent();
            Init();
        }

        private void Init()
        {
            this.lblServerId.Text = NamedPipedIpcClient.Default_B.ServerId.ToString();
            this.lblClientId.Text = NamedPipedIpcClient.Default_B.ClientId.ToString();
            this.btnStart.Text = NamedPipedIpcClient.Default_B.Started ? "停止(&S)" : "启动(&S)";
            this.lblServerStatus.Text = NamedPipedIpcClient.Default_B.Started ? "已启动" : "未启动";
            this.lblClientStatus.Text = NamedPipedIpcClient.Default_B.ClientConnected ? "已连接" : "未连接";
            this.lblQQGroupNo.Text = QQMsgCollectorPlugin._monitorQQGroupNo.ToString();
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            Init();
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (NamedPipedIpcClient.Default_B.Started)
            {
                NamedPipedIpcClient.Default_B.Stop();
            }
            else
            {
                NamedPipedIpcClient.Default_B.Start();
                NamedPipedIpcClient.Default_B.Recieve += QQMsgCollectorPlugin.Ipc_Recieve;
            }
            this.Init();
        }

        private void btnShowLog_Click(object sender, EventArgs e)
        {
            //FormLog.Instance.Show();
        }
    }
}
