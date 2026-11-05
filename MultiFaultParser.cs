using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static mircoInverterTool.Form1;

namespace mircoInverterTool
{
    internal class MultiFaultParser
    {
        public class FaultBitItem
        {
            /// <summary>
            /// 全局总bit位，例如：
            /// 第0个寄存器bit0 → TotalBit=0
            /// 第0个寄存器bit15 → TotalBit=15
            /// 第1个寄存器bit0 → TotalBit=16
            /// 第1个寄存器bit15 → TotalBit=31
            /// 第2个寄存器bit0 → TotalBit=32
            /// </summary>
            /// <summary>该故障所在modbus寄存器地址</summary>
            public ushort RegisterAddress { get; set; }
            public int BitInRegister { get; set; }
            public string FaultDesc { get; set; }
        }

        // key: 组合键 $"{RegisterAddress}_{BitInRegister}"
        public static HashSet<string> _lastActiveFaultKeySet = new HashSet<string>();

        public static List<FaultBitItem> ModbusFaultConfig = new List<FaultBitItem>()
        {
            // ========== reg0：CMPSS_Low16  Addr=1000 ==========
            new FaultBitItem{RegisterAddress = 30030, BitInRegister = 0, FaultDesc = "INV_OCP 逆变过流"},
            new FaultBitItem{RegisterAddress = 30030, BitInRegister = 1, FaultDesc = "LLC_HV_OCP LLC高压侧过流"},
            new FaultBitItem{RegisterAddress = 30030, BitInRegister = 2, FaultDesc = "PV3_OCP PV3过流"},
            new FaultBitItem{RegisterAddress = 30030, BitInRegister = 3, FaultDesc = "PV4_OCP PV4过流"},
            new FaultBitItem{RegisterAddress = 30030, BitInRegister = 4, FaultDesc = "PV2_OCP PV2过流"},
            new FaultBitItem{RegisterAddress = 30030, BitInRegister = 5, FaultDesc = "PV1_OCP PV1过流"},

            // ========== reg1：Grid_Exception Addr=1001 ==========
            new FaultBitItem{RegisterAddress = 30033, BitInRegister = 0, FaultDesc = "AC_OVP 交流过压异常"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister = 1, FaultDesc = "AC_UVP 交流欠压异常"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister = 2, FaultDesc = "AC_OFP 交流过频异常"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister = 3, FaultDesc = "AC_UFP 交流欠频异常"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister = 4, FaultDesc = "AC_LOST_MAIN 市电掉电异常"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister = 5, FaultDesc = "AC_ROCOF 频率变化率异常"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister = 6, FaultDesc = "AC_OCP 交流过流异常"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister = 7, FaultDesc = "AC_OLP 交流过载异常"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister = 8, FaultDesc = "AC_CBC 交流逐波限流"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister = 9, FaultDesc = "AC_ISLAND 孤岛异常"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister =10, FaultDesc = "AC_10MIN_OVP 10分钟过压"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister =11, FaultDesc = "AC_LVRT 低电压穿越"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister =12, FaultDesc = "Grid_RELAY_CHECK 电网继电器校验异常"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister =13, FaultDesc = "Fast_OnOff_Switch 快速开关机异常"},
            new FaultBitItem{RegisterAddress = 30033, BitInRegister =14, FaultDesc = "AC_PLL_FAILED 锁相环失败异常"},

            // ========== reg2：EPS_Exception Addr=1002 ==========
            new FaultBitItem{RegisterAddress = 30034, BitInRegister = 0, FaultDesc = "EPS_OVP EPS输出过压"},
            new FaultBitItem{RegisterAddress = 30034, BitInRegister = 1, FaultDesc = "EPS_UVP EPS输出欠压"},
            new FaultBitItem{RegisterAddress = 30034, BitInRegister = 2, FaultDesc = "EPS_OCP EPS输出过流"},
            new FaultBitItem{RegisterAddress = 30034, BitInRegister = 3, FaultDesc = "EPS_SCP EPS输出短路保护"},
            new FaultBitItem{RegisterAddress = 30034, BitInRegister = 4, FaultDesc = "EPS_OLP EPS输出过载"},
            new FaultBitItem{RegisterAddress = 30034, BitInRegister = 5, FaultDesc = "EPS_START_FAIL EPS启动失败"},
            new FaultBitItem{RegisterAddress = 30034, BitInRegister = 6, FaultDesc = "EPS_GRID_REV EPS电网反接"},
            new FaultBitItem{RegisterAddress = 30034, BitInRegister = 7, FaultDesc = "EPS_RELAY_CHECK EPS继电器校验异常"},

            // ========== reg3：INV_Exception Addr=1003 ==========
            new FaultBitItem{RegisterAddress = 30035, BitInRegister = 0, FaultDesc = "HV_BUS_OVP_SW 高压母线软件过压"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister = 1, FaultDesc = "HV_BUS_UVP_SW 高压母线软件欠压"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister = 2, FaultDesc = "INV_OTP 逆变过温异常"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister = 3, FaultDesc = "AC_ADC 交流采样异常"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister = 4, FaultDesc = "ISO 绝缘阻抗异常"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister = 5, FaultDesc = "LLC_LV_OTP LLC低压侧过温"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister = 6, FaultDesc = "LLC_HV_OTP LLC高压侧过温"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister = 7, FaultDesc = "INV_CURR_OCP_HW 逆变电流硬件过流"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister = 8, FaultDesc = "INV_CURR_OCP_SW 逆变电流软件过流"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister = 9, FaultDesc = "LLC_HV_OCP LLC高压侧过流"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister =10, FaultDesc = "PFC_START PFC启动异常"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister =11, FaultDesc = "DCDC_START DCDC启动异常"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister =12, FaultDesc = "DCDC_PRECHG DCDC预充电异常"},
            new FaultBitItem{RegisterAddress = 30035, BitInRegister =13, FaultDesc = "REMOTE_EXCEPTION 远程控制异常"},

            // ========== reg4：FATAL_Exception Addr=1004 ==========
            new FaultBitItem{RegisterAddress = 30036, BitInRegister = 0, FaultDesc = "LV_BUS_OVP_SW 低压母线软件过压"},
            new FaultBitItem{RegisterAddress = 30036, BitInRegister = 1, FaultDesc = "LV_BUS_UVP_SW 低压母线软件欠压"},
            new FaultBitItem{RegisterAddress = 30036, BitInRegister = 2, FaultDesc = "COMM_LOST_SW 通信丢失异常"},
            new FaultBitItem{RegisterAddress = 30036, BitInRegister = 3, FaultDesc = "RETRACKING_TIMEOUT 重捕获超时异常"},
            new FaultBitItem{RegisterAddress = 30036, BitInRegister = 4, FaultDesc = "REMOTE_FM_UPGRADE 远程固件升级异常"},

            // ========== reg5：PV1_Exception Addr=1005 ==========
            new FaultBitItem{RegisterAddress = 30038, BitInRegister = 0, FaultDesc = "PV1_OVP PV过压异常"},
            new FaultBitItem{RegisterAddress = 30038, BitInRegister = 1, FaultDesc = "PV1_UVP PV欠压异常"},
            new FaultBitItem{RegisterAddress = 30038, BitInRegister = 2, FaultDesc = "PV1_OCP_SW PV软件过流异常"},
            new FaultBitItem{RegisterAddress = 30038, BitInRegister = 3, FaultDesc = "PV1_OCP_HW PV硬件过流异常"},
            new FaultBitItem{RegisterAddress = 30038, BitInRegister = 4, FaultDesc = "PV1_OTP PV过温异常"},
            new FaultBitItem{RegisterAddress = 30038, BitInRegister = 5, FaultDesc = "PV1_REV PV反接异常"},

            // ========== reg6：PV2_Exception Addr=1006 ==========
            new FaultBitItem{RegisterAddress = 30039, BitInRegister = 0, FaultDesc = "PV2_OVP PV过压异常"},
            new FaultBitItem{RegisterAddress = 30039, BitInRegister = 1, FaultDesc = "PV2_UVP PV欠压异常"},
            new FaultBitItem{RegisterAddress = 30039, BitInRegister = 2, FaultDesc = "PV2_OCP_SW PV软件过流异常"},
            new FaultBitItem{RegisterAddress = 30039, BitInRegister = 3, FaultDesc = "PV2_OCP_HW PV硬件过流异常"},
            new FaultBitItem{RegisterAddress = 30039, BitInRegister = 4, FaultDesc = "PV2_OTP PV过温异常"},
            new FaultBitItem{RegisterAddress = 30039, BitInRegister = 5, FaultDesc = "PV2_REV PV反接异常"},

            // ========== reg7：PV3_Exception Addr=1007 ==========
            new FaultBitItem{RegisterAddress = 30040, BitInRegister = 0, FaultDesc = "PV3_OVP PV过压异常"},
            new FaultBitItem{RegisterAddress = 30040, BitInRegister = 1, FaultDesc = "PV3_UVP PV欠压异常"},
            new FaultBitItem{RegisterAddress = 30040, BitInRegister = 2, FaultDesc = "PV3_OCP_SW PV软件过流异常"},
            new FaultBitItem{RegisterAddress = 30040, BitInRegister = 3, FaultDesc = "PV3_OCP_HW PV硬件过流异常"},
            new FaultBitItem{RegisterAddress = 30040, BitInRegister = 4, FaultDesc = "PV3_OTP PV过温异常"},
            new FaultBitItem{RegisterAddress = 30040, BitInRegister = 5, FaultDesc = "PV3_REV PV反接异常"},

            // ========== reg8：PV4_Exception Addr=1008 ==========
            new FaultBitItem{RegisterAddress = 30041, BitInRegister = 0, FaultDesc = "PV4_OVP PV过压异常"},
            new FaultBitItem{RegisterAddress = 30041, BitInRegister = 1, FaultDesc = "PV4_UVP PV欠压异常"},
            new FaultBitItem{RegisterAddress = 30041, BitInRegister = 2, FaultDesc = "PV4_OCP_SW PV软件过流异常"},
            new FaultBitItem{RegisterAddress = 30041, BitInRegister = 3, FaultDesc = "PV4_OCP_HW PV硬件过流异常"},
            new FaultBitItem{RegisterAddress = 30041, BitInRegister = 4, FaultDesc = "PV4_OTP PV过温异常"},
            new FaultBitItem{RegisterAddress = 30041, BitInRegister = 5, FaultDesc = "PV4_REV PV反接异常"},
        };

        public static List<FaultBitItem> M102M103FaultConfig = new List<FaultBitItem>()
        {
            // ========== PV1故障：低1010，高1011 ==========
            new FaultBitItem{RegisterAddress = 1011, BitInRegister = 0, FaultDesc = "PV1 过流保护"},
            new FaultBitItem{RegisterAddress = 1011, BitInRegister = 1, FaultDesc = "PV1 过压保护"},
            new FaultBitItem{RegisterAddress = 1011, BitInRegister = 2, FaultDesc = "PV1 欠压保护"},
            new FaultBitItem{RegisterAddress = 1011, BitInRegister = 5, FaultDesc = "PV1 过温保护"},
            new FaultBitItem{RegisterAddress = 1011, BitInRegister = 6, FaultDesc = "PV1 低温保护"},

            // ========== PV2故障：低1012，高1013 ==========
            new FaultBitItem{RegisterAddress = 1013, BitInRegister = 0, FaultDesc = "PV2 过流保护"},
            new FaultBitItem{RegisterAddress = 1013, BitInRegister = 1, FaultDesc = "PV2 过压保护"},
            new FaultBitItem{RegisterAddress = 1013, BitInRegister = 2, FaultDesc = "PV2 欠压保护"},
            new FaultBitItem{RegisterAddress = 1013, BitInRegister = 5, FaultDesc = "PV2 过温保护"},
            new FaultBitItem{RegisterAddress = 1013, BitInRegister = 6, FaultDesc = "PV2 低温保护"},

            // ========== PV3故障：低1014，高1015 ==========
            new FaultBitItem{RegisterAddress = 1015, BitInRegister = 0, FaultDesc = "PV3 过流保护"},
            new FaultBitItem{RegisterAddress = 1015, BitInRegister = 1, FaultDesc = "PV3 过压保护"},
            new FaultBitItem{RegisterAddress = 1015, BitInRegister = 2, FaultDesc = "PV3 欠压保护"},
            new FaultBitItem{RegisterAddress = 1015, BitInRegister = 5, FaultDesc = "PV3 过温保护"},
            new FaultBitItem{RegisterAddress = 1015, BitInRegister = 6, FaultDesc = "PV3 低温保护"},

            // ========== PV4故障：低1016，高1017 ==========
            new FaultBitItem{RegisterAddress = 1017, BitInRegister = 0, FaultDesc = "PV4 过流保护"},
            new FaultBitItem{RegisterAddress = 1017, BitInRegister = 1, FaultDesc = "PV4 过压保护"},
            new FaultBitItem{RegisterAddress = 1017, BitInRegister = 2, FaultDesc = "PV4 欠压保护"},
            new FaultBitItem{RegisterAddress = 1017, BitInRegister = 5, FaultDesc = "PV4 过温保护"},
            new FaultBitItem{RegisterAddress = 1017, BitInRegister = 6, FaultDesc = "PV4 低温保护"},

            // ========== AC故障：低1018，高1019 ==========
            new FaultBitItem{RegisterAddress = 1019, BitInRegister = 0, FaultDesc = "AC 过流保护 AC_OC"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister = 1, FaultDesc = "AC 过压保护 AC_OV"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister = 2, FaultDesc = "AC 欠压保护 AC_UV"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister = 3, FaultDesc = "AC 过频保护 AC_OF"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister = 4, FaultDesc = "AC 欠频保护 AC_UF"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister = 5, FaultDesc = "AC 过温保护 AC_OT"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister = 6, FaultDesc = "AC 低温保护 AC_UT"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister = 7, FaultDesc = "漏电流报错 GFDI"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister = 8, FaultDesc = "Bus过压保护 Bus_OV"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister = 9, FaultDesc = "交流电丢失 Lost_AC"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister =10, FaultDesc = "远程开关机 Remote_Switch"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister =11, FaultDesc = "低压穿越 LVRT"},
            new FaultBitItem{RegisterAddress = 1019, BitInRegister =12, FaultDesc = "继电器故障 RLY_err"},
        };

        /// 兼容故障寄存器数量不固定、格式不统一；读到哪个解析哪个，未读到直接跳过
        /// </summary>
        /// <param name="allRegList">完整寄存器列表（含RawValue）</param>
        public static void ParseFaultDynamic(List<RegisterItem> allRegList,ComboBox cboModel,RichTextBox richTextBoxFaultLog)
        {
            if (allRegList == null || allRegList.Count == 0) return;

            HashSet<string> currentActiveFaultKeys = new HashSet<string>();
            List<FaultBitItem> _currentFaultConfig;
            if (cboModel.Text.Contains("M102") || cboModel.Text.Contains("M103"))
            {
                _currentFaultConfig = M102M103FaultConfig;
            }
            else
            {
                _currentFaultConfig = ModbusFaultConfig;
            }
            foreach (var faultCfg in _currentFaultConfig)
            {
                // 在本次读到的寄存器列表中查找这个故障所属寄存器
                var regItem = allRegList.FirstOrDefault(r => r.Address == faultCfg.RegisterAddress);
                if (regItem == null)
                {
                    // 本次没有读到该故障寄存器，跳过，不处理
                    continue;
                }

                //拿到本寄存器原始ushort
                ushort regVal = (ushort)regItem.Value;
                bool bitSet = ((regVal >> faultCfg.BitInRegister) & 0x01) != 0;
                string faultKey = $"{faultCfg.RegisterAddress}_{faultCfg.BitInRegister}";

                if (bitSet)
                {
                    currentActiveFaultKeys.Add(faultKey);
                    //新触发故障
                    if (!_lastActiveFaultKeySet.Contains(faultKey))
                    {
                        string log = $"【故障触发】{DateTime.Now:HH:mm:ss}｜{faultCfg.FaultDesc}\r\n";
                        richTextBoxFaultLog.Invoke(new Action(() =>
                        {
                            richTextBoxFaultLog.AppendText(log);
                            richTextBoxFaultLog.ScrollToCaret();
                        }));
                    }
                }
            }

            //处理故障消失：上一轮存在，本轮没有
            foreach (var oldKey in _lastActiveFaultKeySet)
            {
                if (!currentActiveFaultKeys.Contains(oldKey))
                {
                    //拆分key拿回地址和bit
                    var parts = oldKey.Split('_');
                    ushort oldAddr = ushort.Parse(parts[0]);
                    int oldBit = int.Parse(parts[1]);

                    var clearFault = _currentFaultConfig
                        .FirstOrDefault(c => c.RegisterAddress == oldAddr && c.BitInRegister == oldBit);
                    if (clearFault != null)
                    {
                        string log = $"【故障清除】{DateTime.Now:HH:mm:ss}｜{clearFault.FaultDesc}\r\n";
                        richTextBoxFaultLog.Invoke(new Action(() =>
                        {
                            richTextBoxFaultLog.AppendText(log);
                            richTextBoxFaultLog.ScrollToCaret();
                        }));
                    }
                }
            }

            //更新记忆
            _lastActiveFaultKeySet = currentActiveFaultKeys;
        }
    }
}
