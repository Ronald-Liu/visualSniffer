﻿using System;
using System.Collections.Generic;
using System.Linq;
using SharpPcap;
using System.Text;

namespace VisualSniffer
{
    public delegate void newPacket(ref sPacket p);
    public class sPacket
    {
        public DateTime timestamp;
        public Type finalType;
        public PacketDotNet.Packet packet;
        public sPacket(ref PacketDotNet.Packet p)
        {
            timestamp = DateTime.Now;

            PacketDotNet.Packet c;
            for (c = p; c.PayloadPacket != null; c = c.PayloadPacket) ;
            finalType = c.GetType();

            packet = p;
        }
    }

    public enum listenerStatus
    {
        online,
        offline
    }

    public class packetListener
    {
        public event newPacket onParseComplete;
        public List<sPacket> packList;
        private packetListener() { packetArrivalEvent = new PacketArrivalEventHandler(packetArrive); packList = new List<sPacket>(); }
        public static packetListener Instance = new packetListener();
        private PacketArrivalEventHandler packetArrivalEvent;
        public int[] devList;
        public listenerStatus status = listenerStatus.offline;

        private packetFilter onlineFilter = null;
        private string mFilterString;
        private Dictionary<packetFilter, pyDissector> pyDissectorList = new Dictionary<packetFilter, pyDissector>();

        public string filterString
        {
            get { return mFilterString; }
            set { mFilterString = value; }
        }

        public void startCapture()
        {
            if (devList == null)
                foreach (ICaptureDevice dev in CaptureDeviceList.Instance)
                {
                    dev.OnPacketArrival += packetArrivalEvent;
                    dev.Open();
                    dev.StartCapture();
                }
            else
                startCapture(devList);

        }

        public void parkPyDissector(pyDissector dis)
        {
            pyDissectorList[dis.myFilter] = dis;
        }

        public void startCapture(int[] list)
        {
            foreach (var i in list)
                startCapture(i);
        }

        public void startCapture(int index)
        {
            ICaptureDevice dev = CaptureDeviceList.Instance[index];

            dev.OnPacketArrival += packetArrivalEvent;

            dev.Open();
            if (mFilterString != null)
                dev.Filter = mFilterString;
            dev.StartCapture();
            status = listenerStatus.online;
        }

        public void stopCapture()
        {
            foreach (ICaptureDevice dev in CaptureDeviceList.Instance)
                if (dev.Started)
                {
                    try
                    {
                        dev.OnPacketArrival -= packetArrivalEvent;
                        dev.StopCapture();
                        dev.Close();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                }

            status = listenerStatus.offline;
        }

        public void applyOnlineFilter(packetFilter f)
        {
            var needResume = (this.status == listenerStatus.online);

            onlineFilter = f;
            foreach (var i in packList)
                if (f == null || f.pass(ref (i.packet)))
                {
                    var p = i;
                    onParseComplete(ref p);
                }
        }

        void packetArrive(object sender, SharpPcap.CaptureEventArgs packet)
        {
            PacketDotNet.Packet v = PacketDotNet.Packet.ParsePacket(packet.Packet.LinkLayerType, packet.Packet.Data);
            sPacket pp = new sPacket(ref v);
            packList.Add(pp);
            var typeStr = pp.finalType.Name;
            foreach (var i in pyDissectorList)
                if (i.Key.pass(ref v))
                {
                    HLPacket tmpPacket;
                    if (i.Value.parsePacket(v, out tmpPacket))
                    {
                        pp.finalType = typeof(HLPacket);
                        typeStr = tmpPacket.packetType;
                    }
                }

            if (_packetTypeCnt.ContainsKey(typeStr))
                _packetTypeCnt[typeStr] += 1;
            else
                _packetTypeCnt[typeStr] = 1;

            if (onlineFilter == null || onlineFilter.pass(ref v))
                onParseComplete(ref pp);
        }

        private Dictionary<string, uint> _packetTypeCnt = new Dictionary<string, uint>();
        public Dictionary<string, uint> packetTypeCnt
        {
            get { return _packetTypeCnt; }
        }

        public uint packetCnt
        {
            get
            {
                uint tmp = 0;
                foreach (ICaptureDevice dev in CaptureDeviceList.Instance)
                    if (dev.Started)
                        tmp += dev.Statistics.ReceivedPackets;
                return tmp;
            }
        }
    }
}
