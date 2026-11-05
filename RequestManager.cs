using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static mircoInverterTool.Form1;

namespace mircoInverterTool
{
    public class RequestManager
    {
        private readonly object _lock = new object();
        private List<Form1.PendingRequest> _pendingList = new List<PendingRequest>();

        //收到解析好的完整帧，做匹配
        public void OnIncomingFrame(ParsedMbFrame frame)
        {
            lock (_lock)
            {
                //清理已经超时的请求
                _pendingList.RemoveAll(p => DateTime.Now > p.ExpireTime);

                var match = _pendingList.FirstOrDefault(p => p.Slave == frame.Slave && p.FuncCode == frame.FuncCode);
                if (match != null)
                {
                    _pendingList.Remove(match);
                    match.OnComplete?.Invoke(frame, null);
                }
            }
        }

        /// <summary>登记一个待等待请求，发送报文前调用</summary>
        public void RegisterPending(PendingRequest req)
        {
            lock (_lock)
            {
                //清理超时
                _pendingList.RemoveAll(p => DateTime.Now > p.ExpireTime);
                _pendingList.Add(req);
            }
        }

        /// <summary>定时清理超时，建议窗体Timer周期性调用</summary>
        public void CleanTimeout()
        {
            lock (_lock)
            {
                var timeoutItems = _pendingList.Where(p => DateTime.Now > p.ExpireTime).ToList();
                foreach (var t in timeoutItems)
                {
                    t.OnComplete?.Invoke(null, new TimeoutException("通信超时"));
                }
                _pendingList.RemoveAll(p => DateTime.Now > p.ExpireTime);
            }
        }
    }
}
