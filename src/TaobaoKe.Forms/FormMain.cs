﻿using Newtonsoft.Json;
using System;
using System.Collections.Specialized;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using TaobaoKe.Common.Models;
using TaobaoKe.Core.IPC;
using TaobaoKe.Core.Log;
using TaobaoKe.Core.Util;
using TaobaoKe.Forms.Settings;
using TaobaoKe.Forms.Util;
using TaobaoKe.Repository;

namespace TaobaoKe.Forms
{
    [ComVisible(true)]
    public partial class FormMain : FormBase
    {
        DataTable _dataSource = null;
        private string _htmlEditValue = string.Empty;
        TransmitTaskRepository _transmitTaskRepository = new TransmitTaskRepository();
        Regex _regexCQImage = new Regex(@"\[CQ:image,file=(.+)\]");
        bool _transmitting = false;
        bool _monitorStarted = false;
        bool _transmitStarted = false;
        private bool _suspendPreview = false;
        Regex _regexUrl = new Regex(@"(http|ftp|https):\/\/[\w\-_]+(\.[\w\-_]+)+([\w\-\.,@?^=%&amp;:/~\+#]*[\w\-\@?^=%&amp;/~\+#])?");
        Regex _regexTaoToken = new Regex(@"￥\w+￥");
        //private readonly string _detailItemUrl = "https://detail.tmall.com/item.htm?id=";

        public FormMain()
        {
            InitializeComponent();
            InitializeControl();
        }

        #region 初始化

        private void InitializeControl()
        {
            this.statusStartAt.Text = DateTime.Now.ToString() + "开始运行";
            this.imgList.ImageSize = new System.Drawing.Size(24, 24);
            this.imgList.TransparentColor = System.Drawing.Color.Transparent;
            this.imgList.ColorDepth = System.Windows.Forms.ColorDepth.Depth24Bit;
            this.imgList.Images.Add("transmit", Properties.Resources.transmit);
            this.imgList.Images.Add("add_task", Properties.Resources.add_task);
            this.imgList.Images.Add("copy", Properties.Resources.copy);
            this.tpageTransmit.ImageKey = "transmit";
            this.tpageAddTask.ImageKey = "add_task";
            //this.btnCopy.ImageKey = "copy";

            this.ResetTransmitButtonState();
            this.ResetMonitorButtonState();

            this.wbTransmit.Url = new System.Uri(Constants.HtmlEditorPath);
            this.wbTransmit.ObjectForScripting = this;

            _dataSource = new DataTable("Master");
            _dataSource.Columns.Add("RowNo", typeof(int));
            _dataSource.Columns.Add("Id", typeof(int));
            _dataSource.Columns.Add("Content", typeof(string));
            _dataSource.Columns.Add("From", typeof(string));
            _dataSource.Columns.Add("CommissionRate", typeof(decimal));
            _dataSource.Columns.Add("Coupon", typeof(string));
            _dataSource.Columns.Add("CreateTime", typeof(DateTime));
            bsTasks.DataSource = _dataSource;
            bsTasks.DataMember = "";

            LoadUntransmittedTasks();
        }

        private void LoadUntransmittedTasks()
        {
            _dataSource.BeginLoadData();
            try
            {
                foreach (var item in _transmitTaskRepository.GetUntransmittedTasks())
                {
                    AddToDataSource(item);
                }
                ResetRowNo();
            }
            finally
            {
                _dataSource.EndLoadData();
            }
            _dataSource.AcceptChanges();
        }

        #endregion

        #region HtmlEditor

        public string HtmlEditValue
        {
            get
            {
                return GetContent();
            }
            set
            {
                _htmlEditValue = value;
                SetDetailContent();
            }
        }

        // 此方法为kindeditor必须声明的方法
        public string GetContent()
        {
            return _htmlEditValue;
        }

        // 此方法为kindeditor必须声明的方法
        public void SetDetailContent()
        {
            wbTransmit.Document.InvokeScript("setContent", new object[] { _htmlEditValue });
        }

        // 此方法为kindeditor必须声明的方法
        public void RequestContent(string str)
        {
            _htmlEditValue = str;
        }

        private void wbTransmit_Resize(object sender, EventArgs e)
        {
            this.wbTransmit.Refresh();
        }

        #endregion

        #region 采集相关

        private void lnkMonitor_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (_monitorStarted)
            {
                StopMonitor();
            }
            else
            {
                StartMonitor();
            }
            ResetMonitorButtonState();
        }

        private void StartMonitor()
        {
            CheckCQProcess();
            if (!NamedPipedIpcClient.Default_A.Started)
            {
                NamedPipedIpcClient.Default_A.Start();
                NamedPipedIpcClient.Default_A.Recieve += Ipc_Recieve;
            }
            _monitorStarted = true;
        }

        private void StopMonitor()
        {
            if (NamedPipedIpcClient.Default_A.Started)
            {
                NamedPipedIpcClient.Default_A.Stop();
            }
            _monitorStarted = false;
        }

        private void ResetMonitorButtonState()
        {
            if (_monitorStarted)
            {
                this.lnkMonitor.Text = "停止采集";
                this.lnkMonitor.LinkColor = Color.OrangeRed;
                this.statusMonitor.Text = "采集中";
                this.statusMonitor.ForeColor = Color.DodgerBlue;
            }
            else
            {
                this.lnkMonitor.Text = "启动采集";
                this.lnkMonitor.LinkColor = Color.DodgerBlue;
                this.statusMonitor.Text = "未采集";
                this.statusMonitor.ForeColor = Color.OrangeRed;
            }
        }

        private void CheckCQProcess()
        {
            if (Process.GetProcessesByName("CQA").Length == 0)
                Process.Start(Constants.CQPath);
            if (Process.GetProcessesByName("Flexlive.CQP.CSharpProxy").Length == 0)
                Process.Start(Constants.CQProxyPath);
        }

        string Ipc_Recieve(IpcArgs args)
        {
            if (args.Content == "$GetMonitorQQGroupNo$")
            {
                return GlobalSetting.Instance.MonitorSetting.QQGroupNo;
            }
            else
            {
                QQMessage qqMessage = JsonConvert.DeserializeObject<QQMessage>(args.Content);
                LogHelper.Log(LogLevel.INFO, LogItemType.Receive, qqMessage.ToString());
                AddTask(qqMessage);
                return "";
            }
        }

        private void btnAddTask_Click(object sender, EventArgs e)
        {
            AddTask(false);
        }

        private void btnAddTaskAtOnce_Click(object sender, EventArgs e)
        {
            AddTask(true);
            Transmit();
        }

        private void AddTask(bool addToTop)
        {
            if (!string.IsNullOrEmpty(this.HtmlEditValue))
            {
                QQMessage qqMessage = new QQMessage()
                {
                    Message = this.HtmlEditValue
                };
                AddTask(qqMessage, addToTop);
                this.HtmlEditValue = string.Empty;
                this.tabMain.SelectedTab = this.tpageTransmit;
            }
        }

        private void AddTask(QQMessage qqMessage, bool addToTop = false)
        {

            TransmitTask transmitTask = new TransmitTask()
            {
                Content = qqMessage.Message,
                From = qqMessage.fromGroup > 0 ? qqMessage.fromGroup.ToString() : "手工添加",
                CreateTime = DateTime.Now,
                Priority = addToTop ? 1 : 0,
                //Coupon
            };
            _transmitTaskRepository.Add(transmitTask);
            AddToDataSource(transmitTask, addToTop, true);
            this.Invoke((EventHandler)delegate
            {
                gridTasks.Refresh();
            });
        }

        #endregion

        #region 转发Timer

        private void linkTransmit_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (_transmitStarted)
            {
                StopTransmit();
            }
            else
            {
                StartTransmit();
            }
            ResetTransmitButtonState();
        }

        private void StartTransmit()
        {
            timerTransmit.Interval = GlobalSetting.Instance.TransmitSetting.TransmitInterval * 1000;
            timerTransmit.Start();
            _transmitStarted = true;
        }

        private void StopTransmit()
        {
            timerTransmit.Stop();
            _transmitStarted = false;
        }

        private void ResetTransmitButtonState()
        {
            if (_transmitStarted)
            {
                this.lnkTransmit.Text = "停止转发";
                this.lnkTransmit.LinkColor = Color.OrangeRed;
                this.statusTransmit.Text = "转发中";
                this.statusTransmit.ForeColor = Color.DodgerBlue;
            }
            else
            {
                this.lnkTransmit.Text = "启动转发";
                this.lnkTransmit.LinkColor = Color.DodgerBlue;
                this.statusTransmit.Text = "未转发";
                this.statusTransmit.ForeColor = Color.OrangeRed;
            }
        }

        private void timerTransmit_Tick(object sender, EventArgs e)
        {
            Transmit();
        }

        private void Transmit()
        {
            if (!_transmitting && _dataSource.Rows.Count > 0)
            {
                _transmitting = true;
                try
                {
                    DataRow row = _dataSource.Rows[0];
                    TransmitTask transmitTask = GetTransmitTaskFromRow(row);
                    Transmit(transmitTask);
                    _transmitTaskRepository.UpdateStatus(transmitTask.Id);
                    _dataSource.Rows.Remove(row);
                    ResetRowNo();
                    this.gridTasks.Refresh();
                }
                finally
                {
                    _transmitting = false;
                }
            }
        }

        void Transmit(TransmitTask transmitTask)
        {
            string tempImagePath = string.Empty;
            string msg = ParseCQMessage(transmitTask.Content, out tempImagePath);
            // 打开群快捷方式，进行粘贴
            if (string.IsNullOrEmpty(GlobalSetting.Instance.TransmitSetting.QQGroupLnkPath))
            {
                throw new Exception("群快捷方式路径为空，请在全局设置中设置");
            }
            else
            {
                DirectoryInfo dir = new DirectoryInfo(GlobalSetting.Instance.TransmitSetting.QQGroupLnkPath);
                this.Invoke((EventHandler)delegate
                {
                    foreach (FileInfo file in dir.GetFiles())
                    {
                        // 转链接
                        string qqGroupName = Path.GetFileNameWithoutExtension(file.Name);
                        string transformedMsg = UrlTransform(msg, qqGroupName);
                        // 复制到剪切板
                        this.Invoke((EventHandler)delegate
                        {
                            ClipboardHelper.CopyToClipboard(transformedMsg, "");
                        });
                        using (Process.Start(file.FullName))
                        {
                            Thread.Sleep(GlobalSetting.Instance.TransmitSetting.SleepInterval);
                            SendKeys.Send("^v"); // Paster
                            Thread.Sleep(GlobalSetting.Instance.TransmitSetting.SleepInterval);
                            SendKeys.Send("%s"); // Send
                        }
                    }
                });
            }
            if (File.Exists(tempImagePath))
                File.Delete(tempImagePath);

            LogHelper.Log(LogLevel.INFO, LogItemType.Transmit, string.Format("任务ID{0}转发成功", transmitTask.Id));
        }

        #endregion

        #region 转换相关

        private void btnUrlTrans_Click(object sender, EventArgs e)
        {
            _htmlEditValue = UrlTransform(_htmlEditValue);
            SetDetailContent();
        }

        private string UrlTransform(string message, string qqGroupName = "")
        {
            if (!string.IsNullOrEmpty(message))
            {
                MatchCollection matchesUrl = _regexUrl.Matches(message);
                string activityId = string.Empty, itemId = string.Empty;
                if (matchesUrl.Count == 1)
                {
                    string ulandTaobaoUrl = string.Empty;
                    Match matchUrl = matchesUrl[0];
                    string url = matchUrl.Value;
                    using (var response = WebRequestHelper.GetWebResponse(url))
                    {
                        ulandTaobaoUrl = response.ResponseUri.ToString();
                    }
                    if (!string.IsNullOrEmpty(ulandTaobaoUrl))
                    {
                        string[] paramValues = WebRequestHelper.GetQueryParamValues(ulandTaobaoUrl, "activityId", "itemId");
                        activityId = paramValues[0];
                        itemId = paramValues[1];
                    }
                    string towInOneLink = DoUrlTransform(qqGroupName, activityId, itemId);
                    //将长链接转短连接
                    string source = "1681459862";//新浪接口要求的请求者标识,暂时写死这个,有访问次数风险
                    string shortUrl = ShortUrlConvert.Convert(towInOneLink, source);
                    message = message.Replace(matchUrl.Value, shortUrl);
                    // 淘口令
                    //message = _regexTaoToken.Replace(message, transformResult.TaoToken);
                }
                else if (matchesUrl.Count == 3)
                {
                    // 共找到 3 处匹配：
                    //http://img0.qingtaoke.com/www/img/goods/20170113/524855101182.jpg
                    //https://shop.m.taobao.com/shop/coupon.htm?seller_id=2227168127&amp;activityId=3ce6dd5e39df4d648afb032dddad8397
                    //https://detail.tmall.com/item.htm?id=524855101182
                    Match matchActivity = matchesUrl[1];
                    Match matchItemId = matchesUrl[2];
                    activityId = WebRequestHelper.GetQueryParamValue(matchActivity.Value, "activityId");
                    itemId = WebRequestHelper.GetQueryParamValue(matchItemId.Value, "id");
                    string towInOneLink = DoUrlTransform(qqGroupName, activityId, itemId);
                    //将长链接转短连接
                    string source = "1681459862";//新浪接口要求的请求者标识,暂时写死这个,有访问次数风险
                    string shortUrl = ShortUrlConvert.Convert(towInOneLink, source);
                    int start = message.LastIndexOf('>', matchActivity.Index) + 1;
                    int end = message.IndexOf("<br", matchItemId.Index);
                    string msg = string.Format("领优惠券下单地址：{0}", shortUrl);
                    message = message.Remove(start, end - start).Insert(start, msg);
                    //message = message.Replace(matchUrl.Value, shortUrl);
                }
            }
            return message;
        }

        private string DoUrlTransform(string qqGroupName, string activityId, string itemId)
        {
            string adZoneId = GetAdZoneId(qqGroupName);
            if (string.IsNullOrEmpty(adZoneId))
            {
                throw new Exception("推广位未设置，请检查淘客设置");
            }
            String src = "huacai";//标示请求源,不影响链接转化
            String dx = "1";//是否定向,不影响链接转化
            String pId = AlimamaAPI.GetPId(adZoneId);
            TwoInOneLink twoInOneLink = new TwoInOneLink(activityId, pId, itemId, src, dx);
            return twoInOneLink.GenerateLink();
            //TransformParam transformParam = new TransformParam()
            //{
            //    SiteId = siteAdZone.SiteId,
            //    AdZoneId = siteAdZone.AdZoneId,
            //    PromotionURL = _detailItemUrl + itemId,
            //    T = DateUtil.GetUnixTimestamp(),
            //    PvId = "0",
            //    TbToken = _alimamaTbToken,
            //    InputCharset = "utf-8"

            //};
            //return AlimamaUrlTrans.Transform(transformParam, _alimamaCookie);
        }

        private string GetAdZoneId(string qqGroupName)
        {
            string result = null;
            if (!string.IsNullOrEmpty(qqGroupName))
            {
                GlobalSetting.Instance.TaokeSetting.QQGroupAdZones.TryGetValue(qqGroupName, out result);
            }
            if (result == null)
                result = GlobalSetting.Instance.TaokeSetting.DefaultAdZoneId;
            return result;
        }

        #endregion

        #region 其它事件

        private void lnkSetting_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            FormSetting formSetting = new FormSetting();
            if (DialogResult.OK == formSetting.ShowDialog())
            {
                timerTransmit.Interval = GlobalSetting.Instance.TransmitSetting.TransmitInterval * 1000;
                try
                {
                    NamedPipedIpcClient.Default_A.Send(new IpcArgs(GlobalSetting.Instance.MonitorSetting.QQGroupNo));
                }
                catch
                {
                    // 酷Q代理未启动的情况，不需要抛出异常
                }
            }
        }

        private void gridTasks_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex == 1) // 转发内容
            {
                e.Value = _regexCQImage.Replace(e.Value.ToString(), "[图片]");
            }
            else if (e.ColumnIndex == 5) // 创建时间
            {
                e.Value = e.Value.ToString();
            }
        }

        private void gridTasks_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex == 1 && e.RowIndex > -1)
            {
                DataRowView rowView = (DataRowView)this.bsTasks[e.RowIndex];
                string content = Convert.ToString(rowView["Content"]);
                if (!FormPreview.Instance.Visible)
                    FormPreview.Instance.Show();
                string tempImagePath;
                FormPreview.Instance.DocumentText = ParseCQMessage(content, out tempImagePath);
            }
        }

        private void tabMain_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.tabMain.SelectedTab == this.tpageTransmit)
            {
                if (_suspendPreview && !FormPreview.Instance.Visible)
                {
                    FormPreview.Instance.Show();
                    _suspendPreview = false;
                }
            }
            else
            {
                if (FormPreview.Instance.Visible)
                {
                    FormPreview.Instance.Hide();
                    _suspendPreview = true;
                }
            }
        }

        private void statusShowLog_Click(object sender, EventArgs e)
        {
            FormLog.Instance.Show();
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (DialogResult.Cancel == MessageBox.Show("确定要退出淘宝客？", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Question))
            {
                e.Cancel = true;
            }
        }

        private void FormMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            timerTransmit.Dispose();
            Application.Exit();
        }

        #endregion

        #region 私有方法

        private void ResetRowNo()
        {
            for (int i = 0; i < _dataSource.Rows.Count; i++)
            {
                _dataSource.Rows[i]["RowNo"] = i + 1;
            }
        }

        private void AddToDataSource(TransmitTask transmitTask, bool addToTop = false, bool resetRowNo = false)
        {
            DataRow newRow = _dataSource.NewRow();
            newRow["Id"] = transmitTask.Id;
            newRow["Content"] = transmitTask.Content;
            newRow["From"] = transmitTask.From;
            newRow["CommissionRate"] = transmitTask.CommissionRate;
            newRow["Coupon"] = transmitTask.Coupon;
            newRow["CreateTime"] = transmitTask.CreateTime;
            if (addToTop)
                _dataSource.Rows.InsertAt(newRow, 0);
            else
                _dataSource.Rows.Add(newRow);

            if (resetRowNo)
            {
                if (addToTop)
                    ResetRowNo();
                else
                {
                    if (_dataSource.Rows.Count > 1)
                    {
                        newRow["RowNo"] = Convert.ToInt32(_dataSource.Rows[_dataSource.Rows.Count - 2]["RowNo"]) + 1;
                    }
                    else
                    {
                        newRow["RowNo"] = 1;
                    }
                }
            }
        }

        private TransmitTask GetTransmitTaskFromRow(DataRow row)
        {
            return new TransmitTask()
            {
                Id = Convert.ToInt32(row["Id"]),
                Content = Convert.ToString(row["Content"]),
                From = Convert.ToString(row["From"]),
                CommissionRate = Convert.ToInt32(row["CommissionRate"]),
                Coupon = Convert.ToString(row["Coupon"]),
                CreateTime = Convert.ToDateTime(row["CreateTime"])
            };
        }

        // 替换CQ消息为html文本（cqimg图片）
        private string ParseCQMessage(string msg, out string tempImagePath)
        {
            Match match = _regexCQImage.Match(msg);
            tempImagePath = string.Empty;
            if (match.Success && match.Groups.Count > 1)
            {
                string imageName = match.Groups[1].Value;
                tempImagePath = Path.Combine(Constants.CQImagePath, imageName);
                string iniFilePath = tempImagePath + ".cqimg";
                string imageUrl = IniFileUtil.ReadIniData("image", "url", "", iniFilePath);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    if (!File.Exists(tempImagePath))
                        WebRequestHelper.DownloadFile(imageUrl, tempImagePath);
                    msg = msg.Remove(match.Index, match.Length);
                    msg = msg.Insert(match.Index, string.Format("<img src='file:///{0}' height='200'>", tempImagePath));
                }
            }
            return msg.Replace("\r\n", "<br />");
        }

        #endregion
    }
}
