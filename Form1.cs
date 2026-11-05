using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static mircoInverterTool.MultiFaultParser;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;

namespace mircoInverterTool
{
    public partial class Form1 : Form
    {
        #region 串口接口
        /// <summary>通用发送接口，串口/蓝牙都实现这个</summary>
        public interface ICommTransport
        {
            /// <summary>原始字节发送</summary>
            void SendBytes(byte[] data);
            bool IsOpen { get; }
        }

        // 串口实现
        public class SerialTransport : ICommTransport
        {
            private SerialPort _port;
            public bool IsOpen => _port != null && _port.IsOpen;

            public SerialTransport(SerialPort port)
            {
                _port = port;
            }

            public void SendBytes(byte[] data)
            {
                if (!IsOpen) throw new InvalidOperationException("串口未打开");
                _port.Write(data, 0, data.Length);
            }
        }
        #endregion

        #region 查询下发
        /// <summary>下发的待请求记录，用于匹配应答</summary>
        public class PendingRequest
        {
            public byte Slave { get; set; }
            public byte FuncCode { get; set; }
            public DateTime ExpireTime { get; set; }
            public int ExpectRegCount { get; set; } //预期返回寄存器数量
                                                    //应答到达回调
            public Action<ParsedMbFrame, Exception> OnComplete { get; set; }
        }
        #endregion

        #region 机型协议结构体
        /// <summary>
        /// 机型与支持协议映射
        /// </summary>
        public class MachineProtocolItem
        {
            /// <summary>机器系列名称，下拉框显示</summary>
            public string MachineSeries { get; set; }
            /// <summary>该机型支持的协议列表</summary>
            public List<string> ProtocolList { get; set; } = new List<string>();
        }

        //窗体成员，保存全部机型数据
        private List<MachineProtocolItem> _machineProtoList = new List<MachineProtocolItem>();
        //保存上一次读到的数值：key=寄存器地址，value=上一次的解析数值
        private Dictionary<int, double> _lastRegValueDict = new Dictionary<int, double>();
        #endregion

        //窗体成员变量，保存全部寄存器表格
        private List<RegisterItem> _regTable = new List<RegisterItem>();
        public class RegisterItem
        {
            public ushort Address { get; set; }
            public string Desc { get; set; }
            public double Scale { get; set; }
            public bool ValueType { get; set; }
            public double Value { get; set; }
        }

        /// <summary>通信协议模式</summary>
        public enum CommProtocolMode
        {
            ModbusRtuRaw,       // 裸Modbus‑RTU，无外层7F7F/F7F7
            Custom7F7FWrapper   // 自定义封装：7F7F + 头 + Modbus + CRC + F7F7
        }

        public enum MbRespType
        {
            Unknown,
            ReadSingleReg,       // 读单个寄存器应答，1个寄存器 byteCount=0x02
            ReadBatchRegisters,  // 读多个寄存器应答，多寄存器 byteCount>0x02
            WriteSingleReg,      // 读单个寄存器应答，1个寄存器 byteCount=0x02
            WriteBatchRegisters  // 读多个寄存器应答，多寄存器 byteCount>0x02
        }

        public class ParsedMbFrame
        {
            public CommProtocolMode ProtocolMode { get; set; }
            public MbRespType RespType { get; set; }
            public byte Slave { get; set; }
            public byte FuncCode { get; set; }
            public ushort[] RegData { get; set; }
            public byte[] RawOuterFrame { get; set; }   //完整外层原始帧
            public byte[] RawInnerMb { get; set; }      //内层Modbus RTU帧
            public ushort Address { get; set; }
            public ushort RegNumber { get; set; }
        }

        // 全局接收缓冲区（解决分段粘包）
        private readonly object _rxLock = new object();
        private List<byte> _rxBuffer = new List<byte>();

        // 两组帧头 帧尾
        private readonly byte[] HEAD_7F = new byte[] { 0x7F, 0x7F };
        private readonly byte[] TAIL_7F = new byte[] { 0xF7, 0xF7 };

        public static string[] BaudRateArr = { "110", "300", "600", "1200", "2400", "4800", "9600", "14400", "19200", "38400", "56000", "57600", "115200" };
        public static string[] DataBitsArr = { "6", "7", "8" };
        public static string[] CheckBitsArr = { "None", "奇校验", "偶校验", "Mark", "空格校验" };
        public static string[] StopBitsArr = { "1", "1.5", "2" };
        public static string[] StatusArr = { "工厂模式", "用户模式" };
        public static string[] WorkStatusArr = { "关机", "并网充电", "并网放电", "离网放电" };
        public static bool FinishReceive = true;
        public string selectProto;

        private MbFrameParser _parser;
        private RequestManager _reqMgr;
        private ICommTransport _currentTransport; //可以切换串口/蓝牙

        public Form1()
        {
            InitializeComponent();

            _parser = new MbFrameParser();
            _reqMgr = new RequestManager();
            //解析器产出帧，交给请求管理器做应答匹配
            _parser.OnFrameParsed += frame =>
            {
                _reqMgr.OnIncomingFrame(frame);
                //打印原始报文日志到UI，注意Invoke
                this.Invoke(new Action(() =>
                {
                    switch (frame.RespType)
                    {
                        case MbRespType.WriteSingleReg:
                            richTextBox1.AppendText($"写入成功，从站:{frame.Slave}\r\n");
                            break;
                        case MbRespType.WriteBatchRegisters:
                            richTextBox1.AppendText($"写入成功，从站:{frame.Slave},寄存器数量:{frame.RegNumber}\r\n");
                            break;
                        case MbRespType.ReadSingleReg:
                            //richTextBox1.AppendText($"收到应答，从站:{frame.Slave},功能码:{frame.FuncCode},返回寄存器数:{frame.RegData?.Length}\r\n");
                            UpdateGridDisplayValues();
                            MultiFaultParser.ParseFaultDynamic(_regTable, ProtocolComo, richTextBox2);
                            break;
                        case MbRespType.ReadBatchRegisters:
                            //richTextBox1.AppendText($"收到应答，从站:{frame.Slave},功能码:{frame.FuncCode},返回寄存器数:{frame.RegData?.Length}\r\n");
                            UpdateGridDisplayValues();
                            MultiFaultParser.ParseFaultDynamic(_regTable, ProtocolComo, richTextBox2);
                            break;
                        default:
                            richTextBox1.AppendText("未知指令\r\n");
                            break;
                    }
                }));
            };
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //=====新增，程序启动自动加载机型协议配置txt，替换成你的txt实际路径=====
            string machineConfigPath = Path.Combine(System.Windows.Forms.Application.StartupPath, "machine_protocol.txt");
            LoadMachineProtocolTxt(machineConfigPath);
            //绑定下拉框切换事件（设计器也可以绑定）
            SeriesComo.SelectedIndexChanged += SeriesComo_SelectedIndexChanged;
            checkBox2_CheckedChanged(null,null);
            BaudRateComo.Items.AddRange(BaudRateArr);
            DataBitComo.Items.AddRange(DataBitsArr);
            CheckBitComo.Items.AddRange(CheckBitsArr);
            StopBitComo.Items.AddRange(StopBitsArr);
            statusCombo.Items.AddRange(StatusArr);
            WorkStatusCombo.Items.AddRange(WorkStatusArr);
            SeriesComo.SelectedIndex = 0;
            WorkStatusCombo.SelectedIndex = 0;
            DataBitComo.SelectedIndex = 2;
            CheckBitComo.SelectedIndex = 0;
            StopBitComo.SelectedIndex = 0;
            statusCombo.SelectedIndex = 1;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string Buffer;
            SerialPortComo.Items.Clear();    //清初之前已经扫描的记录
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    Buffer = "COM" + i.ToString(); //获得COM1-20
                    serialPort1.PortName = Buffer;     //获取COM信息
                    serialPort1.Open();                 //打开串口
                    SerialPortComo.Items.Add(Buffer);
                    SerialPortComo.Text = Buffer;                   //添加串口得到记录集
                    serialPort1.Close();                 //关闭串口
                }
                catch { }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (button2.Text == "打开串口")
            {
                try
                {
                    serialPort1.PortName = SerialPortComo.Text;
                    serialPort1.BaudRate = Convert.ToInt32(BaudRateComo.Text, 10);
                    serialPort1.DataBits = Convert.ToInt32(DataBitComo.Text, 10);
                    if (CheckBitComo.Text == "None") { serialPort1.Parity = Parity.None; }
                    else if (CheckBitComo.Text == "奇校验") { serialPort1.Parity = Parity.Odd; }
                    else if (CheckBitComo.Text == "偶校验") { serialPort1.Parity = Parity.Even; }
                    else if (CheckBitComo.Text == "Mark") { serialPort1.Parity = Parity.Mark; }
                    else if (CheckBitComo.Text == "空格校验") { serialPort1.Parity = Parity.Space; }
                    if (StopBitComo.Text == "1")
                    {
                        serialPort1.StopBits = StopBits.One;
                    }
                    else if (StopBitComo.Text == "1.5")
                    {
                        serialPort1.StopBits = StopBits.OnePointFive;
                    }
                    else if (StopBitComo.Text == "1.5")
                    {
                        serialPort1.StopBits = StopBits.Two;
                    }
                    //serialPort1.ReadTimeout(2000);
                    serialPort1.Open();
                    button2.Text = "关闭串口";
                }
                catch
                {
                    MessageBox.Show("端口错误,请检查串口", "错误");
                }
            }
            else
            {
                try
                {
                    serialPort1.Close();
                    button2.Text = "打开串口";
                }
                catch
                {
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            string txtPath;
            if (button3.Text == "选定协议")
            {
                if (SeriesComo.SelectedIndex < 1)
                {
                    string[] modelArr = { "600", "800", "1000", "1200" };
                    modelComo.Items.AddRange(modelArr);
                    modelComo.SelectedIndex = 3;
                }
                else
                {
                    string[] modelArr = { "1200", "1600", "2000", "2400", "2500" };
                    modelComo.Items.AddRange(modelArr);
                    modelComo.SelectedIndex = 4;
                }
                if (ProtocolComo.SelectedItem.ToString() == "Modbus")
                {
                    BaudRateComo.SelectedIndex = 9;
                    regCnt.Text = "22";
                    commGap.Text = "1";
                }
                else 
                {
                    BaudRateComo.SelectedIndex = 6;
                    regCnt.Text = "13";
                    commGap.Text = "1";
                }
                txtPath = Path.Combine(System.Windows.Forms.Application.StartupPath, ProtocolComo.Text + ".txt");
                if (!File.Exists(txtPath))
                {
                    MessageBox.Show("未找到对应协议，请放到程序exe同一文件夹！");
                    return;
                }
                try
                {
                    // 读取所有行
                    string[] lines = File.ReadAllLines(txtPath);
                    //跳过第一行表头
                    foreach (var line in lines.Skip(1))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        //按空白字符分割，兼容多个空格
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 3) continue;

                        var item = new RegisterItem
                        {
                            Address = ushort.Parse(parts[0]),
                            Desc = parts[1],
                            ValueType = bool.Parse(parts[2]),
                            Scale = int.Parse(parts[3]),
                            Value = 0 //txt里面第四列是空，初始未赋值
                        };
                        _regTable.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("读取文件失败：" + ex.Message);
                }
                //====新增：查找 PV1电压读取 的地址，填充到起始地址文本框====
                if (_regTable == null || _regTable.Count == 0)
                {
                    MessageBox.Show("请先导入寄存器TXT文件！");
                    return;
                }
                //查找描述为 PV1电压读取 的寄存器
                var pv1Item = _regTable.FirstOrDefault(x => x.Desc.Trim() == "PV1电压");

                if (pv1Item != null)
                {
                    //把地址赋值给右侧起始地址输入框
                    startAddr.Text = pv1Item.Address.ToString();
                }
                else
                {
                    MessageBox.Show("寄存器列表中没有找到【PV1电压】条目，请检查txt配置文件！");
                }
                ShowWholeRegTable();
                selectProto = ProtocolComo.SelectedItem.ToString();
                SeriesComo.Enabled = false;
                ProtocolComo.Enabled = false;
                button3.Text = "切换协议";
            }
            else
            {
                selectProto = "";
                _regTable.Clear();
                modelComo.Items.Clear();
                SeriesComo.Enabled = true;
                ProtocolComo.Enabled = true;
                button3.Text = "选定协议";
            }

        }

        private void serialPort1_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            int cnt = serialPort1.BytesToRead;
            byte[] buf = new byte[cnt];
            serialPort1.Read(buf, 0, cnt);
            //喂给通用解析器
            _parser.FeedBytes(buf);
        }


        private void button6_Click(object sender, EventArgs e)
        {
            double output = FillSingleRegister(50606, 16384);
            voltageset.Text = output.ToString();
        }

        private void button7_Click(object sender, EventArgs e)
        {

        }

        private void button8_Click(object sender, EventArgs e)
        {
            double vactive, vset;
            try
            {
                vactive = double.Parse(voltageactive.Text.Trim());
                vset = double.Parse(voltageset.Text.Trim());
            }
            catch 
            {
                MessageBox.Show("请输入合法数字！");
                return;
            }
            var target = findTarget("AC电压校准系数");
            ushort writeData = Convert.ToUInt16((vset * target.Scale)/ vactive);
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button9_Click(object sender, EventArgs e)
        {
            var target = findTarget("AC电压校准系数");
            ushort writeData = 16384;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void SeriesComo_SelectedIndexChanged(object sender, EventArgs e)
        {
            ProtocolComo.Items.Clear();
            ProtocolComo.Text = "";

            string selectMachine = SeriesComo.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectMachine)) return;

            //查找选中机型对应的协议集合
            var findItem = _machineProtoList.FirstOrDefault(m => m.MachineSeries == selectMachine);
            if (findItem == null) return;

            foreach (var p in findItem.ProtocolList)
            {
                ProtocolComo.Items.Add(p);
            }
            //默认选中第一项（可选）
            if (ProtocolComo.Items.Count > 0)
                ProtocolComo.SelectedIndex = 0;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            _reqMgr.CleanTimeout();
        }

        //----------------------------------------//

        /// <summary>
        /// 单个寄存器填充
        /// </summary>
        /// <param name="addr">寄存器地址</param>
        /// <param name="raw2Bytes">2字节原始modbus数据，大端，长度必须等于2</param>
        /// <returns>是否找到并更新成功</returns>
        public double FillSingleRegister(int addr, ushort rawVal)
        {
            //查找内存列表对应地址
            var target = _regTable.FirstOrDefault(x => x.Address == addr);
            if (target == null)
            {
                //UI提示未找到该地址
                this.Invoke(new Action(() =>
                {
                    richTextBox1.AppendText($"【警告】未找到寄存器地址：{addr}\r\n");
                }));
                return -1;
            }
            if (target.ValueType)
            {
                short irawVal = (short)rawVal;   //直接把16bit二进制重新解读成有符号数，不会抛异常
                target.Value = (double)irawVal / target.Scale;
            }
            else 
            {
                //核心计算：原始值 / 倍数
                target.Value = (double)rawVal;
            }

            return target.Value;
        }

        /// <summary>
        /// 批量连续寄存器填充
        /// </summary>
        /// <param name="startAddr">起始寄存器地址</param>
        /// <param name="regCount">寄存器个数</param>
        /// <param name="totalBytes">modbus返回完整字节数组，长度=regCount*2</param>
        public void FillBatchRegisters(int startAddr, int regCount, ushort[] totalBytes)
        {
            if (totalBytes == null || totalBytes.Length != regCount)
                throw new ArgumentException($"批量数据长度错误，预期{regCount}字节，实际{totalBytes?.Length}");

            for (int i = 0; i < regCount; i++)
            {
                int currentAddr = startAddr + i;
                //直接复用上面写好的单寄存器填充逻辑
                FillSingleRegister(currentAddr, totalBytes[i]);
            }
            this.Invoke(new Action(() =>
            {
                //richTextBox1.AppendText($"====批量更新完成，起始地址{startAddr}，共{regCount}个寄存器====\r\n");
            }));
        }

        public void ShowWholeRegTable()
        {
            this.Invoke(new Action(() =>
            {
                richTextBox1.Clear();
                richTextBox1.AppendText($"====全部寄存器表格====\r\n");
                foreach (var item in _regTable)
                {
                    richTextBox1.AppendText($"地址:{item.Address,-6} | {item.Desc}\r\n");
                }
            }));
        }

        /// <summary>
        /// 读取机型协议txt，解析到内存集合，绑定机器系列下拉框comboBox1
        /// </summary>
        /// <param name="filePath">txt完整路径</param>
        public void LoadMachineProtocolTxt(string filePath)
        {
            _machineProtoList.Clear();
            SeriesComo.Items.Clear();
            ProtocolComo.Items.Clear();

            if (!File.Exists(filePath))
            {
                MessageBox.Show($"机型配置文件不存在：{filePath}");
                return;
            }

            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                string trimLine = line.Trim();
                if (string.IsNullOrEmpty(trimLine)) continue;

                //按冒号分割 机型 : 协议内容
                string[] colonSplit = trimLine.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (colonSplit.Length < 2) continue;

                string machineName = colonSplit[0].Trim();
                string protoRawText = colonSplit[1].Trim();

                //按中文顿号 、分割多个协议，去除每个协议前后空格
                var protos = protoRawText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                var item = new MachineProtocolItem
                {
                    MachineSeries = machineName,
                    ProtocolList = protos
                };
                _machineProtoList.Add(item);
                //添加到第一个下拉框（机器系列）
                SeriesComo.Items.Add(machineName);
            }
        }

        private RegisterItem findTarget(string Desc) 
        {
            var target = _regTable.FirstOrDefault(x => x.Desc == Desc);
            if (target == null)
            {
                //UI提示未找到该地址
                this.Invoke(new Action(() =>
                {
                    richTextBox1.AppendText($"【警告】未找到寄存器地址：{Desc}\r\n");
                }));
                return null;
            }
            return target;
        }

        #region SCIRead&Write
        public void readReg(ushort addr,ushort regCnt) 
        {
            byte slave;
            var protoMode = CommProtocolMode.Custom7F7FWrapper;
            _currentTransport = new SerialTransport(serialPort1);
            if (_currentTransport.IsOpen == false)
            {
                MessageBox.Show("通道未打开");
                return;
            }
            if (ProtocolComo.Text == "Modbus")
            {
                slave = 0xD1;
                protoMode = CommProtocolMode.ModbusRtuRaw;
            }
            else 
            {
                slave = 0x01;
                protoMode = CommProtocolMode.Custom7F7FWrapper;
            }
            byte[] sendBuf = MbFrameParser.BuildMb03Packet(slave, addr, regCnt, protoMode);
            var pending = new PendingRequest
            {
                Slave = slave,
                FuncCode = 0x03,
                ExpireTime = DateTime.Now.AddMilliseconds(1000),
                ExpectRegCount = Convert.ToInt32(regCnt),
                OnComplete = (frame, ex) =>
                {
                    Invoke(new Action(() =>
                    {
                        if (ex != null)
                        {
                            label1.Text = $"异常:{ex.Message}";
                            return;
                        }
                        label1.Text = $"收到应答，更新时间:{DateTime.Now.ToString("hh:mm:ss")}\r\n";
                        FillBatchRegisters(addr, regCnt, frame.RegData);
                        //*****************************************************
                    }));
                }
            };
            _reqMgr.RegisterPending(pending);
            _currentTransport.SendBytes(sendBuf);
        }

        public void writeReg(ushort addr, ushort regCnt, ushort[] writeData)
        {
            byte slave;
            var protoMode = CommProtocolMode.Custom7F7FWrapper;
            _currentTransport = new SerialTransport(serialPort1);
            if (_currentTransport.IsOpen == false)
            {
                MessageBox.Show("通道未打开");
                return;
            }
            if (regCnt != writeData.Length) 
            {
                MessageBox.Show("数据长度不对应");
                return;
            }
            if (ProtocolComo.Text == "Modbus")
            {
                slave = 0xD1;
                protoMode = CommProtocolMode.ModbusRtuRaw;
            }
            else
            {
                slave = 0x01;
                protoMode = CommProtocolMode.Custom7F7FWrapper;
            }
            byte[] sendBuf = MbFrameParser.BuildMb0610Packet(addr, regCnt, writeData, protoMode);
            byte funcCode = (byte)(regCnt > 1 ? 0x10 : 0x06);
            var pending = new PendingRequest
            {
                Slave = slave,
                FuncCode = funcCode,
                ExpireTime = DateTime.Now.AddMilliseconds(1000),
                ExpectRegCount = Convert.ToInt32(regCnt),
                OnComplete = (frame, ex) =>
                {
                    Invoke(new Action(() =>
                    {
                        if (ex != null)
                        {
                            richTextBox1.AppendText($"异常:{ex.Message}\r\n");
                            return;
                        }
                        richTextBox1.AppendText($"收到帧，协议:{frame.ProtocolMode},应答类型:{frame.RespType}\r\n");
                        //*****************************************************
                    }));
                }
            };
            _reqMgr.RegisterPending(pending);
            _currentTransport.SendBytes(sendBuf);
        }
        #endregion

        private void button11_Click(object sender, EventArgs e)
        {            
            ushort addr = Convert.ToUInt16(startAddr.Text);
            ushort regcnt = Convert.ToUInt16(regCnt.Text);


            //切换显示区间，清空上一轮历史值缓存
            _lastRegValueDict.Clear();

            //后面是你原来的代码不变
            if (_regTable == null || _regTable.Count == 0) return;
                

            FilterRegGridByRange(addr, regcnt);
            if (timer2.Enabled)
            {
                timer2.Stop();
                button11.Text = "开始读取";
                return;
            }

            // 读取文本框，单位：秒
            if (!double.TryParse(commGap.Text.Trim(), out double sec))
            {
                MessageBox.Show("定时间隔输入格式错误，请输入数字！");
                return;
            }

            // 最小间隔0.1秒，防止间隔太小串口风暴
            if (sec < 0.1)
            {
                MessageBox.Show("定时间隔不能小于0.1秒");
                return;
            }

            // Timer.Interval单位是毫秒，秒*1000
            int ms = (int)(sec * 1000);
            timer2.Interval = ms;

            // 启动定时器
            timer2.Start();
            button11.Text = "停止读取";
        }

        #region gridview控制
        /// <summary>
        /// 根据起始地址、寄存器数量，筛选出连续片段，生成DataGridView行（只执行切换参数时）
        /// </summary>
        /// <param name="startAddr">批量起始地址</param>
        /// <param name="regCount">寄存器个数</param>
        private void FilterRegGridByRange(int startAddr, int regCount)
        {
            if (_regTable == null || _regTable.Count == 0)
                return;

            if (RegShowGridView.InvokeRequired)
            {
                RegShowGridView.Invoke(new Action(() => FilterRegGridByRange(startAddr, regCount)));
                return;
            }

            int endAddr = startAddr + regCount - 1;
            RegShowGridView.Rows.Clear();
            RegShowGridView.Columns.Clear();
            RegShowGridView.ReadOnly = true;
            RegShowGridView.AllowUserToAddRows = false;
            RegShowGridView.RowHeadersVisible = false;

            //列2 参数描述
            RegShowGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ColDesc",
                HeaderText = "参数描述",
                Width = 220
            });

            //列4 解析后数值
            RegShowGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ColValue",
                HeaderText = "解析数值",
                Width = 130
            });

            //开启双缓冲，消除闪烁，写在这里
            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.SetProperty,
                null,
                RegShowGridView,
                new object[] { true });

            //筛选落在区间内的寄存器，并且按地址从小到大排序
            var sliceItems = _regTable
                .Where(r => r.Address >= startAddr && r.Address <= endAddr)
                .OrderBy(r => r.Address)
                .ToList();

            foreach (var item in sliceItems)
            {
                int idx = RegShowGridView.Rows.Add();
                var row = RegShowGridView.Rows[idx];
                row.Cells["ColDesc"].Value = item.Desc;
                row.Cells["ColValue"].Value = "未读取";
                row.Tag = item; //把RegisterItem挂到行Tag，后面快速更新值
            }
        }

        /// <summary>
        /// 收到应答后，仅更新当前grid已经展示行的Value单元格，不新增删除行
        /// </summary>
        private void UpdateGridDisplayValues()
        {
            if (RegShowGridView.InvokeRequired)
            {
                RegShowGridView.Invoke(new Action(UpdateGridDisplayValues));
                return;
            }

            foreach (DataGridViewRow row in RegShowGridView.Rows)
            {
                var regItem = row.Tag as RegisterItem;
                if (regItem == null) continue;
                if (regItem.ValueType)
                {
                    row.Cells["ColValue"].Value = regItem.Value.ToString();
                }
                else
                {
                    row.Cells["ColValue"].Value = regItem.Value.ToString("F3");
                }
                /**********/
                /************/
                int addr = regItem.Address;
                double currentVal = regItem.Value;
                // 不存在旧记录：第一次加载，直接保存，白底
                if (!_lastRegValueDict.ContainsKey(addr))
                {
                    row.Cells["ColValue"].Style.BackColor = Color.White;
                    _lastRegValueDict[addr] = currentVal;
                }
                else
                {
                    double oldVal = _lastRegValueDict[addr];
                    // 判断数值是否发生变化（浮点允许极小误差）
                    if (Math.Abs(currentVal - oldVal) > 0.001)
                    {
                        //数值发生变化 → 黄色底色
                        row.Cells["ColValue"].Style.BackColor = Color.Yellow;
                        _lastRegValueDict[addr] = currentVal; //更新旧值
                    }
                    else
                    {
                        //数值不变，恢复白底
                        row.Cells["ColValue"].Style.BackColor = Color.White;
                    }

                }
            }
        }

        /// <summary>
        /// DataGridView更新完成后执行：收集所有fault描述的寄存器，做故障解析
        /// </summary>
        private void SelectRegulation(string str)
        {
            dataGridView1.Rows.Clear();
            //1.找出所有Desc包含fault的RegisterItem
            var faultRegItems = _regTable
                .Where(item => item.Desc != null
                        && item.Desc.IndexOf(str, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (faultRegItems.Count == 0)
            {
                return;
            }

            //2.按寄存器地址从小到大排序，组装ushort数组，顺序必须和故障配置表reg0‑reg8一一对应
            faultRegItems = faultRegItems.OrderBy(x => x.Address).ToList();

            ushort[] faultBuffer = new ushort[faultRegItems.Count];
            foreach (var item in faultRegItems)
            {
                int idx = dataGridView1.Rows.Add();
                var row = dataGridView1.Rows[idx];
                row.Cells["ColDesc"].Value = item.Desc;
                row.Cells["ColValue"].Value = "未读取";
                row.Tag = item; //把RegisterItem挂到行Tag，后面快速更新值
            }
        }

        #endregion

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (timer1 != null)
            {
                timer1.Stop();
            }
            if (timer2 != null)
            {
                timer2.Stop();
            }
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            ushort addr = Convert.ToUInt16(startAddr.Text);
            ushort regcnt = Convert.ToUInt16(regCnt.Text);
            readReg(addr, regcnt);
        }

        private void button10_Click(object sender, EventArgs e)
        {

        }

        private void button12_Click(object sender, EventArgs e)
        {
            var target = findTarget("AC电压校准系数");
            ushort writeData = 16384;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            ushort[] arr = new ushort[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00 };
            MultiFaultParser.ParseFaultDynamic(_regTable, ProtocolComo, richTextBox2);
        }

        private void button5_Click_1(object sender, EventArgs e)
        {

        }

        private void button13_Click(object sender, EventArgs e)
        {

        }

        private void button26_Click(object sender, EventArgs e)
        {
            richTextBox2.Clear();
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox2.Checked)
            {
                textBox5.Enabled = true;
                textBox4.Enabled = true;
            }
            else
            {
                textBox5.Enabled = false;
                textBox4.Enabled = false;
            }
        }

        #region 工作模式控制
        private void button14_Click(object sender, EventArgs e)
        {
            var target = findTarget("充放电工作模式");
            ushort writeData = Convert.ToUInt16(WorkStatusCombo.SelectedIndex * target.Scale);
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button15_Click(object sender, EventArgs e)
        {
            double doub;
            try
            {
                doub = double.Parse(textBox10.Text.Trim());
            }
            catch
            {
                MessageBox.Show("请输入合法数字！");
                return;
            }
            var target = findTarget("逆变器放电功率");
            ushort writeData = Convert.ToUInt16(doub * target.Scale);
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button16_Click(object sender, EventArgs e)
        {
            double doub;
            try
            {
                doub = double.Parse(textBox11.Text.Trim());
            }
            catch
            {
                MessageBox.Show("请输入合法数字！");
                return;
            }
            var target = findTarget("电网充电功率");
            ushort writeData = Convert.ToUInt16(doub * target.Scale);
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button17_Click(object sender, EventArgs e)
        {
            var target = findTarget("旁路继电器开关");
            ushort writeData = 1;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button27_Click(object sender, EventArgs e)
        {
            var target = findTarget("旁路继电器开关");
            ushort writeData = 0;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }
        #endregion

        #region PV开关
        private void button21_Click(object sender, EventArgs e)
        {
            var target = findTarget("PV1开关");
            ushort writeData = 1;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button20_Click(object sender, EventArgs e)
        {
            var target = findTarget("PV1开关");
            ushort writeData = 0;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button29_Click(object sender, EventArgs e)
        {
            var target = findTarget("PV2开关");
            ushort writeData = 1;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button28_Click(object sender, EventArgs e)
        {
            var target = findTarget("PV2开关");
            ushort writeData = 0;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button19_Click(object sender, EventArgs e)
        {
            var target = findTarget("PV3开关");
            ushort writeData = 1;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button18_Click(object sender, EventArgs e)
        {
            var target = findTarget("PV3开关");
            ushort writeData = 0;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button31_Click(object sender, EventArgs e)
        {
            var target = findTarget("PV4开关");
            ushort writeData = 1;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button30_Click(object sender, EventArgs e)
        {
            var target = findTarget("PV4开关");
            ushort writeData = 0;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }
        #endregion

        #region 手动MPPT控制
        private void button23_Click(object sender, EventArgs e)
        {
            var target = findTarget("手动MPPT开关");
            ushort writeData = 1;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }
 

        private void button22_Click(object sender, EventArgs e)
        {
            var target = findTarget("手动MPPT开关");
            ushort writeData = 0;
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }
        private void button24_Click(object sender, EventArgs e)
        {
            double doub;
            try
            {
                doub = double.Parse(textBox12.Text.Trim());
            }
            catch
            {
                MessageBox.Show("请输入合法数字！");
                return;
            }
            var target = findTarget("PV1参考MPPT电压");
            ushort writeData = Convert.ToUInt16(doub * target.Scale);
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button25_Click(object sender, EventArgs e)
        {
            double doub;
            try
            {
                doub = double.Parse(textBox13.Text.Trim());
            }
            catch
            {
                MessageBox.Show("请输入合法数字！");
                return;
            }
            var target = findTarget("PV2参考MPPT电压");
            ushort writeData = Convert.ToUInt16(doub * target.Scale);
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button32_Click(object sender, EventArgs e)
        {
            double doub;
            try
            {
                doub = double.Parse(textBox14.Text.Trim());
            }
            catch
            {
                MessageBox.Show("请输入合法数字！");
                return;
            }
            var target = findTarget("PV3参考MPPT电压");
            ushort writeData = Convert.ToUInt16(doub * target.Scale);
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        private void button33_Click(object sender, EventArgs e)
        {
            double doub;
            try
            {
                doub = double.Parse(textBox15.Text.Trim());
            }
            catch
            {
                MessageBox.Show("请输入合法数字！");
                return;
            }
            var target = findTarget("PV4参考MPPT电压");
            ushort writeData = Convert.ToUInt16(doub * target.Scale);
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);
        }

        #endregion

        private void button5_Click(object sender, EventArgs e)
        {
            SelectRegulation("并网");
        }

        private void button13_Click_1(object sender, EventArgs e)
        {
            SelectRegulation("过压");
        }

        private void dataGridView1_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            //只处理【值】那一列
            if (e.ColumnIndex != 1)
                return;

            int rowIdx = e.RowIndex;
            if (rowIdx < 0 || rowIdx >= dataGridView1.Rows.Count - 1) return;

            string desc = dataGridView1.Rows[rowIdx].Cells[0].Value?.ToString();
            string valueStr = dataGridView1.Rows[rowIdx].Cells[1].Value?.ToString();
            //1.取出本行配置
            if (string.IsNullOrWhiteSpace(desc)) return;

            //解析用户输入的工程值
            if (!float.TryParse(valueStr, out float userShowValue))
            {
                MessageBox.Show("输入格式错误，请输入数字");
                return;
            }
            var target = findTarget(desc);
            // =========核心换算=========
            // 界面显示值 = 原始寄存器值 * Scale
            // → 寄存器原始值 = 显示值 / 倍数Scale
            ushort writeData = Convert.ToUInt16(userShowValue * target.Scale);
            ushort[] writeDataArr = new ushort[] { writeData };
            ushort addr = (ushort)target.Address;
            writeReg(addr, 1, writeDataArr);


            ////限制ushort范围 0~65535
            //if (rawFloat < 0) rawFloat = 0;
            //if (rawFloat > ushort.MaxValue) rawFloat = ushort.MaxValue;

            //ushort regValue = (ushort)Math.Round(rawFloat);
            //ushort[] writeDataArr = new ushort[] { regValue };

            //日志输出
            richTextBox1.AppendText($"\r\n【表格修改下发】地址:{target.Address} 输入工程值:{userShowValue:F2} 倍数:{target.Scale} 寄存器原始值:{userShowValue}");
        }
    }
}
