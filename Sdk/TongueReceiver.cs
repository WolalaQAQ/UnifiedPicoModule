using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
namespace UnifiedPicoModule.Sdk;
// Wi-Fi UDP from the PicoET-Enhance tongue payload (no ADB): send "TSUB" to the headset's
// UDP 19031 about once a second; the headset answers with 24-byte TNG2 datagrams for 3s.
// The PC starts the exchange, so a stateful firewall admits the replies without any rule.
internal sealed class TongueReceiver : IDisposable
{
    private readonly UdpClient socket;
    private readonly Func<IPAddress?> headsetAddress;   // configured Bridge.IP, else the auto-discovered Bridge peer
    private readonly int headsetPort;
    private IPAddress? headset;
    private long lastSubscribe;
    private readonly Action<string> log;
    private readonly string statusPath;
    private long received,reported;
    private uint sequence;
    private float left,right,tongueOut,confidence;
    private bool fallbackActive,negative,uncertain;
    private long lastVisual;
    private float heldLeft,heldRight,heldOut;
    private float puckerHold;
    private long puckerSeen;
    private static readonly byte[] Subscribe={(byte)'T',(byte)'S',(byte)'U',(byte)'B',1,0,0,0};
    public TongueReceiver(Func<IPAddress?> address,Action<string> logger,string diagnosticPath, int port=19031)
    {
        log=logger;statusPath=diagnosticPath;headsetAddress=address;headsetPort=port;
        socket=new UdpClient(new IPEndPoint(IPAddress.Any,0));socket.Client.Blocking=false;
        log("Tongue: Wi-Fi UDP subscription to the Bridge headset (Bridge.IP or auto-discovery) port 19031; timeout350ms.");
    }
    public (bool Fresh,float Left,float Right,float Out) Poll(float stockTongueOut=1,float pucker=0)
    {
        if(lastSubscribe==0||Stopwatch.GetElapsedTime(lastSubscribe).TotalSeconds>=1)
        {
            lastSubscribe=Stopwatch.GetTimestamp();
            var current=headsetAddress();
            if(current is not null&&!current.Equals(headset)){headset=current;log($"Tongue: subscribing to headset {current}:{headsetPort}");}
            if(headset is not null)try{socket.Send(Subscribe,Subscribe.Length,new IPEndPoint(headset,headsetPort));}catch(SocketException){}
        }
        for(int i=0;i<64;i++)
        {
            byte[] p;IPEndPoint? from=null;
            try{if(socket.Available<=0)break;p=socket.Receive(ref from);}catch(SocketException){break;}
            if(p.Length!=24||from is null||headset is null||!from.Address.Equals(headset))continue;
            if(!p.AsSpan(0,4).SequenceEqual("TNG2"u8))continue;
            float l=BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(8)),r=BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(12));
            float o=BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(16)),c=BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(20));
            if(!float.IsFinite(l)||!float.IsFinite(r)||!float.IsFinite(o)||!float.IsFinite(c)||l<0||l>1||r<0||r>1||o<0||o>1||c<0||c>1)continue;
            left=l;right=r;tongueOut=o;confidence=c;uint bits=BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(4));sequence=bits&0x3fffffff;
            negative=(bits&0x80000000)!=0;uncertain=(bits&0x40000000)!=0;received=Stopwatch.GetTimestamp();
            if(!negative&&!uncertain&&o>.5f&&c>.15f){lastVisual=received;heldLeft=l;heldRight=r;heldOut=o;}
        }
        long now=Stopwatch.GetTimestamp();
        bool fresh=received!=0&&Stopwatch.GetElapsedTime(received,now).TotalMilliseconds<350;
        float native=float.IsFinite(stockTongueOut)?Math.Clamp(stockTongueOut,0,1):0;
        bool held=fresh&&!negative&&lastVisual!=0&&Stopwatch.GetElapsedTime(lastVisual,now).TotalMilliseconds<250;
        if(!fresh||negative)fallbackActive=false;
        else if(!uncertain&&confidence>.45f&&tongueOut>.6f)fallbackActive=true;
        else if(!held)fallbackActive=false;
        float fallback=fresh&&fallbackActive?(held?Math.Max(tongueOut,heldOut):tongueOut):0;
        // Lips pushed forward (PICO's own pucker) look like a tongue to the image model: while PICO
        // sees pucker, the model may not create tongue extension; native TongueOut still passes.
        float puckerNow=Math.Clamp((pucker-.30f)/.20f,0,1);
        // Releasing a pucker passes through a half-pucker that still looks like a tongue: keep the
        // strongest recent gate for 400ms after PICO's pucker drops.
        if(puckerNow>=puckerHold||Stopwatch.GetElapsedTime(puckerSeen,now).TotalMilliseconds>400){puckerHold=puckerNow;puckerSeen=now;}
        float puckerGate=Math.Max(puckerNow,puckerHold);
        float combined=Math.Max(native,fallback*(1-puckerGate)); // 05D1A46 baseline: native plus model fallback.
        float gate=Math.Clamp((combined-.05f)/.15f,0,1);
        float modelLeft=uncertain&&held?heldLeft:left,modelRight=uncertain&&held?heldRight:right;
        float finalLeft=fresh?modelLeft*gate:0,finalRight=fresh?modelRight*gate:0;
        if(reported==0||Stopwatch.GetElapsedTime(reported,now).TotalSeconds>=1)
        {
            reported=now;
            try{File.WriteAllText(statusPath,System.Text.Json.JsonSerializer.Serialize(new{sequence,fresh,left=finalLeft,right=finalRight,tongueOut=combined,nativeOut=native,pucker,puckerGate,confidence,negative,uncertain,held,modelOut=fresh?tongueOut:0,transport="UDP",utc=DateTime.UtcNow}));}catch(IOException){}catch(UnauthorizedAccessException){}
            log($"Tongue seq={sequence} fresh={fresh} L={(fresh?left:0):F3} R={(fresh?right:0):F3}");
        }
        return fresh?(true,finalLeft,finalRight,combined):(false,0,0,0);
    }
    public void Dispose()=>socket.Dispose();
}
