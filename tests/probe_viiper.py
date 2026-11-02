"""Exercise the real pinned server without attaching a Windows device or sending keys."""
import json, socket, subprocess, time
from pathlib import Path

root=Path(__file__).resolve().parent.parent
process=subprocess.Popen([str(root/'Updated/HidRuntime/viiper.exe'),'server','--no-tray','--update-notify=none',
    '--usb.addr=127.0.0.1:33241','--api.addr=127.0.0.1:33242','--api.auto-attach-local-client=false'],
    stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=0x08000000)

def request(command):
    with socket.create_connection(('127.0.0.1',33242),timeout=2) as stream:
        stream.sendall((command+'\0').encode())
        data=b''
        while True:
            block=stream.recv(4096)
            if not block:break
            data+=block
        return json.loads(data)

try:
    time.sleep(1)
    assert process.poll() is None, 'server failed to start'
    ping=request('ping')
    assert ping['server']=='VIIPER'
    print('PASS pinned server identity:',ping)
    bus=request('bus/create')['busId']
    device=request(f'bus/{bus}/add '+json.dumps({'type':'keyboard'}))
    assert device['type']=='keyboard' and all(k in device for k in ('vid','pid','devId'))
    print('PASS actual keyboard API schema:',device)
    with socket.create_connection(('127.0.0.1',33242),timeout=2) as stream:
        stream.sendall(f'bus/{bus}/{device["devId"]}\0'.encode())
        stream.sendall(bytes([0,0]))
        print('PASS neutral report submitted over real stream; no USB host attached')
    assert request(f'bus/remove {bus}')['busId']==bus
    print('PASS owned test bus cleanup')
finally:
    process.terminate()
    process.wait(timeout=5)
