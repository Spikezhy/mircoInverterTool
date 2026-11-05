using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static mircoInverterTool.Form1;

namespace mircoInverterTool
{
    public class MbFrameParser
    {
        private readonly object _rxLock = new object();
        private List<byte> _rxBuffer = new List<byte>();
        private readonly byte[] HEAD_7F = { 0x7F, 0x7F };
        private readonly byte[] TAIL_7F = { 0xF7, 0xF7 };

        public event Action<ParsedMbFrame> OnFrameParsed;

        public void FeedBytes(byte[] recvBytes)
        {
            lock (_rxLock)
            {
                _rxBuffer.AddRange(recvBytes);
            }
            ThreadPool.QueueUserWorkItem(_ => ProcessBuffer());
        }

        private void ProcessBuffer()
        {
            lock (_rxLock)
            {
                while (true)
                {
                    //优先判断是否存在自定义协议帧头7F7F
                    int head7fPos = FindSeq(_rxBuffer, HEAD_7F);
                    if (head7fPos >= 0)
                    {
                        if (head7fPos > 0)
                        {
                            _rxBuffer.RemoveRange(0, head7fPos);
                        }
                        //=====【模式1：自定义7F7F封装协议】=====
                        int tailPos = FindSeq(_rxBuffer, TAIL_7F);
                        if (tailPos < 0) break;

                        byte[] outerFrame = _rxBuffer.GetRange(0, tailPos + 2).ToArray();
                        _rxBuffer.RemoveRange(0, tailPos + 2);

                        if (outerFrame.Length < 11) continue;
                        //偏移9字节取出内层Modbus‑RTU
                        byte[] innerMb = new byte[outerFrame.Length - 2 - 2 - 9];
                        Array.Copy(outerFrame, 9, innerMb, 0, innerMb.Length);

                        byte[] crcCheck = new byte[outerFrame.Length - 2 - 2];
                        Array.Copy(outerFrame, 2, crcCheck, 0, crcCheck.Length);
                        //校验Modbus内层CRC
                        ushort calcCrc = CalcModbusCRC(crcCheck.Take(crcCheck.Length - 2).ToArray());
                        ushort recvCrc = (ushort)((crcCheck[crcCheck.Length - 1] << 8) | crcCheck[crcCheck.Length - 2]);
                        if (calcCrc != recvCrc) continue;

                        ParsedMbFrame frame = ParseInnerModbus(innerMb);
                        frame.ProtocolMode = CommProtocolMode.Custom7F7FWrapper;
                        frame.RawOuterFrame = outerFrame;
                        OnFrameParsed?.Invoke(frame);
                    }
                    else
                    {
                        //=====【模式2：裸 Modbus‑RTU】=====
                        //Modbus RTU最小帧长度：从站(1)+功能(1)+字节数(1)+数据(2)+CRC(2) =7字节
                        if (_rxBuffer.Count < 7) break;

                        //扫描缓冲区，尝试截取一帧裸Modbus
                        bool foundRawFrame = false;
                        for (int start = 0; start <= _rxBuffer.Count - 7; start++)
                        {
                            byte slave = _rxBuffer[start];
                            byte func = _rxBuffer[start + 1];
                            int frameLen;

                            if ((func & 0x80) != 0)
                            {
                                //异常应答帧
                                frameLen = 5;
                            }
                            else if (func == 0x03 || func == 0x04)
                            {
                                byte dataLen = _rxBuffer[start + 2];
                                frameLen = 3 + dataLen + 2;
                            }
                            else
                            {
                                continue;
                            }

                            if (start + frameLen > _rxBuffer.Count) continue;

                            byte[] rawMb = _rxBuffer.GetRange(start, frameLen).ToArray();
                            ushort calcCrc = CalcModbusCRC(rawMb.Take(rawMb.Length - 2).ToArray());
                            ushort recvCrc = (ushort)((rawMb[rawMb.Length - 1] << 8) | rawMb[rawMb.Length - 2]);
                            if (calcCrc == recvCrc)
                            {
                                //完整裸Modbus帧
                                _rxBuffer.RemoveRange(0, start + frameLen);
                                ParsedMbFrame frame = ParseInnerModbus(rawMb);
                                frame.ProtocolMode = CommProtocolMode.ModbusRtuRaw;
                                frame.RawOuterFrame = rawMb;
                                OnFrameParsed?.Invoke(frame);
                                foundRawFrame = true;
                                break;
                            }
                        }
                        if (!foundRawFrame) break;
                    }
                }
            }
        }

        /// <summary>解析内层标准Modbus‑RTU帧（03功能码），两套协议共用这段Modbus解析逻辑</summary>
        private ParsedMbFrame ParseInnerModbus(byte[] innerMb)
        {
            ParsedMbFrame frame = new ParsedMbFrame();
            frame.RawInnerMb = innerMb;
            frame.Slave = innerMb[0];
            frame.FuncCode = innerMb[1];

            if (frame.FuncCode == 0x03 || frame.FuncCode == 0x04)
            {
                byte byteCnt = innerMb[2];
                List<ushort> regList = new List<ushort>();
                for (int i = 0; i < byteCnt; i += 2)
                {
                    ushort val = (ushort)((innerMb[3 + i] << 8) | innerMb[4 + i]);
                    regList.Add(val);
                }

                frame.RegData = regList.ToArray();
                frame.RespType = byteCnt == 0x02 ? MbRespType.ReadSingleReg : MbRespType.ReadBatchRegisters;
                frame.RegNumber = (ushort)(byteCnt >> 1);
            }
            else if(frame.FuncCode == 0x06)
            {
                frame.RespType = MbRespType.WriteSingleReg;
                frame.RegData = new ushort[0];
            }
            else if (frame.FuncCode == 0x10)
            {
                frame.RespType = MbRespType.WriteBatchRegisters;
                frame.RegData = new ushort[0];
            }
            else
            {
                frame.RespType = MbRespType.Unknown;
                frame.RegData = new ushort[0];
            }
            return frame;
        }

        private int FindSeq(List<byte> buf, byte[] seq)
        {
            if (buf.Count < seq.Length) return -1;
            for (int i = 0; i <= buf.Count - seq.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < seq.Length; j++)
                {
                    if (buf[i + j] != seq[j]) 
                    {
                        ok = false; break; 
                    }
                }
                if (ok) return i;
            }
            return -1;
        }

        /// <summary>构建03读寄存器报文，可以输出裸ModbusRTU 或者7F7F外层封装报文</summary>
        /// <param name="slave">从站号</param>
        /// <param name="startAddr">起始寄存器地址</param>
        /// <param name="regCount">寄存器数量</param>
        /// <param name="mode">协议模式</param>
        /// <returns>完整待发送字节流</returns>
        public static byte[] BuildMb03Packet(byte slave, ushort startAddr, ushort regCount, CommProtocolMode mode)
        {
            //1、组装标准Modbus‑RTU内层请求帧
            List<byte> mbInner = new List<byte>();
            mbInner.Add(slave);
            mbInner.Add(0x03);
            mbInner.Add((byte)(startAddr >> 8));
            mbInner.Add((byte)(startAddr & 0x00FF));
            mbInner.Add((byte)(regCount >> 8));
            mbInner.Add((byte)(regCount & 0x00FF));
            byte[] modbusRtuFrame = mbInner.ToArray();

            if (mode == CommProtocolMode.ModbusRtuRaw)
            {

                ushort crc = MbFrameParser.CalcModbusCRC(mbInner.ToArray());
                mbInner.Add((byte)(crc & 0x00FF));
                mbInner.Add((byte)(crc >> 8));
                //裸Modbus‑RTU，直接返回
                return mbInner.ToArray();
            }
            else
            {
                //组装自定义7F7F外层封装
                List<byte> outer = new List<byte>();
                outer.Add(0x7F);
                outer.Add(0x7F);
                outer.Add(0x11);
                outer.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x01 });
                outer.Add((byte)(modbusRtuFrame.Length >> 8));
                outer.Add((byte)(modbusRtuFrame.Length & 0x00FF));
                outer.AddRange(modbusRtuFrame);

                //外层头+内容CRC（跳过前2字节7F7F）
                ushort outerCrc = CalcModbusCRC(outer.Skip(2).ToArray());
                outer.Add((byte)(outerCrc & 0xFF));
                outer.Add((byte)(outerCrc >> 8));
                outer.Add(0xF7);
                outer.Add(0xF7);
                return outer.ToArray();
            }
        }

        /// <summary>构建0610读寄存器报文，可以输出裸ModbusRTU 或者7F7F外层封装报文</summary>
        /// <param name="slave">从站号</param>
        /// <param name="startAddr">起始寄存器地址</param>
        /// <param name="regCount">寄存器数量</param>
        /// <param name="mode">协议模式</param>
        /// <returns>完整待发送字节流</returns>
        public static byte[] BuildMb0610Packet(ushort startAddr, ushort regCount, ushort[] writeData, CommProtocolMode mode)
        {
            //1、组装标准Modbus‑RTU内层请求帧
            List<byte> mbInner = new List<byte>();

            if (writeData.Length != regCount)
            {
                MessageBox.Show($"数据个数不对，需要{regCount}个寄存器值");
                return null;
            }

            if (mode == CommProtocolMode.ModbusRtuRaw)
            {
                mbInner.Add(0xD1);
            }
            else
            {
                mbInner.Add(0x01);
            }
            if (regCount > 1)
            {
                mbInner.Add(0x10);  //写多个
                mbInner.Add((byte)(startAddr >> 8));
                mbInner.Add((byte)(startAddr & 0x00FF));
                mbInner.Add((byte)(regCount >> 8));
                mbInner.Add((byte)(regCount & 0x00FF));
            }
            else
            {
                mbInner.Add(0x06);  //写单个
                mbInner.Add((byte)(startAddr >> 8));
                mbInner.Add((byte)(startAddr & 0x00FF));
            }

            //填充寄存器数据（大端）
            foreach (ushort val in writeData)
            {
                mbInner.Add((byte)(val >> 8));
                mbInner.Add((byte)(val & 0xFF));
            }

            byte[] modbusRtuFrame = mbInner.ToArray();

            if (mode == CommProtocolMode.ModbusRtuRaw)
            {
                ushort crc = MbFrameParser.CalcModbusCRC(mbInner.ToArray());
                mbInner.Add((byte)(crc & 0x00FF));
                mbInner.Add((byte)(crc >> 8));
                //裸Modbus‑RTU，直接返回
                return mbInner.ToArray();
            }
            else
            {
                //组装自定义7F7F外层封装
                List<byte> outer = new List<byte>();
                outer.Add(0x7F);
                outer.Add(0x7F);
                outer.Add(0x11);
                outer.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x01 });
                outer.Add((byte)(modbusRtuFrame.Length >> 8));
                outer.Add((byte)(modbusRtuFrame.Length & 0x00FF));
                outer.AddRange(modbusRtuFrame);

                //外层头+内容CRC（跳过前2字节7F7F）
                ushort outerCrc = CalcModbusCRC(outer.Skip(2).ToArray());
                outer.Add((byte)(outerCrc & 0xFF));
                outer.Add((byte)(outerCrc >> 8));
                outer.Add(0xF7);
                outer.Add(0xF7);
                return outer.ToArray();
            }
        }
        public static ushort CalcModbusCRC(byte[] data)
        {
            ushort crc = 0xFFFF;
            foreach (var b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
                }
            }
            return crc;
        }

    }
}
