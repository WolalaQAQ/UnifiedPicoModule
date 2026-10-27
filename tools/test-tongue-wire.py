"""Actual PC-app result_packet -> production C# receiver and module Write; synthetic images only."""
import argparse
import importlib.util
import json
from pathlib import Path
import queue
import shutil
import socket
import subprocess
import sys
import threading
import time
from types import SimpleNamespace

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dll', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--pc-wire', type=Path, required=True, help='pc-tongue/wire.py from the matching PicoET-Enhance branch')
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    original = args.pc_wire
    snapshot = args.output/'pc_wire.py'
    shutil.copy2(original, snapshot)
    spec = importlib.util.spec_from_file_location('pc_wire', snapshot)
    wire = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = wire
    spec.loader.exec_module(wire)
    def encode(session, seq, state=1, age=0.):
        h = SimpleNamespace(session_id=session, source_seq=seq, mouth_sensor_ns=1_000_000_000+seq*11_111_111)
        return wire.result_packet(h, state, .6, .1, .8, age)
    cases = {}
    for no_face in (False, True):
        name = 'foreign-face' if no_face else 'pico-face'
        process = subprocess.Popen([args.dotnet, str(args.dll.resolve()), '--probe', *(['--no-face'] if no_face else [])],
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8')
        lines, messages = [], queue.Queue()
        def read():
            for line in process.stdout:
                lines.append(line)
                messages.put(json.loads(line))
        reader = threading.Thread(target=read, daemon=True)
        reader.start()
        try:
            ready = messages.get(timeout=5)
            assert ready['ready']
            endpoint = ('127.0.0.1', ready['port'])
            rows = []
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender:
                def send(data):
                    sender.sendto(data, endpoint)
                    row = messages.get(timeout=2)
                    rows.append(row)
                    if no_face:
                        assert all(abs(row[k]-.731) < .00001 for k in ('out', 'left', 'right', 'up', 'down')), row
                    return row
                first = send(encode(100, 10))
                if not no_face:
                    assert abs(first['out']-.8) < .00001
                negative = send(encode(100, 11, 2))
                if not no_face:
                    assert negative['out'] == negative['left'] == negative['right'] == 0
                assert not send(encode(100, 10))['accepted']
                positive = encode(200, 1)
                assert send(positive)['accepted']
                due = time.monotonic()+.6
                while time.monotonic() < due:
                    duplicate = send(positive)
                    assert not duplicate['accepted']
                    time.sleep(.01)
                if not no_face:
                    assert abs(duplicate['out']-.17) < .00001
                assert not send(encode(100, 12))['accepted']
                assert send(encode(300, 1))['accepted']
                invalid = send(encode(300, 1, 8))
                if not no_face:
                    assert abs(invalid['out']-.17) < .00001
                assert send(encode(300, 2, age=400))['reason'] == 'stale-source'
                assert send(encode(300, 3)[:25])['reason'] == 'reject-length'
                assert send(encode(300, 2, 16))['accepted']
            assert messages.get(timeout=3)['socket_released']
            process.wait(timeout=3)
            reader.join(1)
            stderr = process.stderr.read()
            assert process.returncode == 0, stderr
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as rebound:
                rebound.bind(endpoint)
            cases[name] = {'passed': True, 'packets': len(rows), 'socket_rebound': True}
            (args.output/(name+'.json')).write_text(json.dumps(rows, indent=2), encoding='utf-8')
            (args.output/(name+'.stdout.log')).write_text(''.join(lines), encoding='utf-8')
            (args.output/(name+'.stderr.log')).write_text(stderr, encoding='utf-8')
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(3)
    result = {'passed': all(v['passed'] for v in cases.values()), 'cases': cases,
              'pc_wire_unchanged': snapshot.read_bytes() == original.read_bytes(),
              'coverage': 'Synthetic TJR1 packets from the actual PC-app encoder to production C# and real SDK Write; no model, headset or host UI'}
    (args.output/'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))
    assert result['pc_wire_unchanged']


if __name__ == '__main__':
    main()
