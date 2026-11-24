"""Actual PC MFR1 encoder -> production receiver/Write, deterministic batch and UDP."""
import argparse
import base64
import importlib.util
import json
from pathlib import Path
import queue
import socket
import subprocess
import sys
import threading
import time
from types import SimpleNamespace


# Independent expected SDK correspondence, not imported from the C# mapper.
TARGETS = [
    ('CheekPuffLeft', 'CheekPuffRight'), ('CheekSquintLeft',), ('CheekSquintRight',),
    ('JawForward',), ('JawLeft',), ('JawOpen',), ('JawRight',), ('MouthClosed',),
    ('MouthDimpleLeft',), ('MouthDimpleRight',), ('MouthFrownLeft',), ('MouthFrownRight',),
    ('LipFunnelUpperLeft', 'LipFunnelUpperRight', 'LipFunnelLowerLeft', 'LipFunnelLowerRight'),
    ('MouthUpperLeft', 'MouthLowerLeft'), ('MouthLowerDownLeft',), ('MouthLowerDownRight',),
    ('MouthPressLeft',), ('MouthPressRight',),
    ('LipPuckerUpperLeft', 'LipPuckerUpperRight', 'LipPuckerLowerLeft', 'LipPuckerLowerRight'),
    ('MouthUpperRight', 'MouthLowerRight'), ('LipSuckLowerLeft', 'LipSuckLowerRight'),
    ('LipSuckUpperLeft', 'LipSuckUpperRight'), ('MouthRaiserLower',), ('MouthRaiserUpper',),
    ('MouthCornerPullLeft', 'MouthCornerSlantLeft'), ('MouthCornerPullRight', 'MouthCornerSlantRight'),
    ('MouthStretchLeft',), ('MouthStretchRight',), ('MouthUpperUpLeft', 'MouthUpperDeepenLeft'),
    ('MouthUpperUpRight', 'MouthUpperDeepenRight'), ('NoseSneerLeft',), ('NoseSneerRight',),
    ('TongueOut',), ('TongueLeft',), ('TongueRight',), ('TongueUp',), ('TongueDown',),
    ('CheekPuffLeft',), ('CheekPuffRight',), ('CheekSuckLeft',), ('CheekSuckRight',),
    ('MouthTightenerLeft',), ('MouthTightenerRight',), ('TongueRoll',),
    ('MouthUpperLeft',), ('MouthUpperRight',), ('MouthLowerLeft',), ('MouthLowerRight',),
    ('LipFunnelUpperLeft', 'LipFunnelUpperRight'), ('LipFunnelLowerLeft', 'LipFunnelLowerRight'),
]
NATIVE = set(name for targets in TARGETS[:33] for name in targets)
EXTENSIONS = set(name for targets in TARGETS[33:] for name in targets) - NATIVE
ALL_TARGETS = NATIVE | EXTENSIONS
UNOWNED = {'JawBackward', 'TongueCurlUp', 'TongueBendDown', 'TongueTwistLeft', 'TongueFlat', 'EyeWideLeft'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dll', type=Path, required=True)
    parser.add_argument('--pc-wire', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', default='C:/Program Files/dotnet/dotnet.exe')
    parser.add_argument('--batch-only', action='store_true')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    original = args.pc_wire.read_bytes()
    sys.path.insert(0, str(args.pc_wire.resolve().parent))
    spec = importlib.util.spec_from_file_location('mouth_wire_under_test', args.pc_wire.resolve())
    wire = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = wire
    spec.loader.exec_module(wire)
    from face_schema import ALL_VALID, CHANNELS, SCHEMA
    assert len(CHANNELS) == len(TARGETS) == 50

    def header(seq, session=100):
        return SimpleNamespace(session_id=session, source_seq=seq, mouth_sensor_ns=1_000_000_000+seq*11_111_111)

    def encode(seq, values, mask, session=100, age=0., state=0):
        return wire.face_result_packet(header(seq, session), values, mask, age, state)

    values = [(index+1)/50 for index in range(50)]
    rows = []
    expected = {name: .731 for name in ALL_TARGETS | UNOWNED}
    owned = set()

    def baseline():
        for name in NATIVE:
            expected[name] = .23 if name == 'JawOpen' else .17 if name == 'TongueOut' else 0.
        for name in EXTENSIONS:
            expected[name] = 0.

    def fresh(packet, mask, controls, **extra):
        nonlocal owned
        # Unset native targets retain the Bridge/pipeline baseline written just before Apply;
        # previously-owned non-native extensions not present in this mask clear to 0.
        for name in NATIVE:
            expected[name] = .23 if name == 'JawOpen' else .17 if name == 'TongueOut' else 0.
        for name in owned:
            expected[name] = 0.
        current = set()
        for index, targets in enumerate(TARGETS):
            if mask & (1 << index):
                for name in targets:
                    expected[name] = controls[index]
                    if name in EXTENSIONS:
                        current.add(name)
        owned = current
        rows.append(dict(packet=base64.b64encode(packet).decode(), accepted=True,
                         expected=expected.copy(), advanceMs=11, **extra))

    for index in range(50):
        controls = [float('nan')] * 50
        controls[index] = values[index]
        fresh(encode(index+1, controls, 1 << index), 1 << index, controls)
    full = encode(51, values, ALL_VALID)
    fresh(full, ALL_VALID, values)
    rows.append(dict(packet=base64.b64encode(full).decode(), accepted=False,
                     reason='duplicate-seq', expected=expected.copy()))
    bad = bytearray(encode(52, values, ALL_VALID)); bad[255] = 255  # Non-finite final supported float.
    rows.append(dict(packet=base64.b64encode(bad).decode(), accepted=False,
                     reason='reject-numeric', expected=expected.copy()))
    # Upper-only must not erase an unsupported Lower; zero Out must not gate directions.
    controls = [0.] * 50
    for name, value in [('MouthUpperLeft', .81), ('TongueLeft', .10), ('TongueUp', .20), ('TongueRoll', .33)]:
        controls[CHANNELS.index(name)] = value
    mask = sum(1 << CHANNELS.index(name) for name in ('MouthUpperLeft', 'TongueOut', 'TongueLeft', 'TongueUp', 'TongueRoll'))
    fresh(encode(52, controls, mask), mask, controls)
    # 6-tongue-only partial mask (0x81F00000000) preserves native JawOpen=.23; explicit off (JawOpen bit=1, val=0) zeros it.
    six_tongue = sum(1 << CHANNELS.index(name) for name in ('TongueOut', 'TongueLeft', 'TongueRight', 'TongueUp', 'TongueDown', 'TongueRoll'))
    assert six_tongue == 0x81F00000000
    t_controls = [0.] * 50
    for name, value in [('TongueOut', .75), ('TongueLeft', .25), ('TongueRight', .15), ('TongueUp', .40), ('TongueDown', .20), ('TongueRoll', .55)]:
        t_controls[CHANNELS.index(name)] = value
    fresh(encode(53, t_controls, six_tongue), six_tongue, t_controls)
    assert expected['JawOpen'] == .23
    fresh(encode(54, t_controls, six_tongue | (1 << CHANNELS.index('JawOpen'))), six_tongue | (1 << CHANNELS.index('JawOpen')), t_controls)
    assert expected['JawOpen'] == 0.
    baseline(); owned.clear()
    rows.append(dict(advanceMs=351, expected=expected.copy()))
    rows.append(dict(packet=base64.b64encode(full).decode(), accepted=False,
                     reason='old-seq', expected=expected.copy()))
    fresh(encode(1, values, ALL_VALID, session=200), ALL_VALID, values)
    rows.append(dict(packet=base64.b64encode(encode(99, values, ALL_VALID)).decode(),
                     accepted=False, reason='retired-session', expected=expected.copy()))
    legacy = wire.result_packet(header(999, 999), 1, .6, .1, .8)
    rows.append(dict(packet=base64.b64encode(legacy).decode(), accepted=False,
                     reason='legacy-after-mouth', expected=expected.copy()))
    baseline(); owned.clear()
    release = encode(1, [float('nan')]*50, 0, session=200)
    rows.append(dict(packet=base64.b64encode(release).decode(), accepted=True,
                     reason='mouth-release', expected=expected.copy()))

    batch = args.output/'batch.jsonl'
    batch.write_text(''.join(json.dumps(row)+'\n' for row in rows), encoding='utf-8')
    result = subprocess.run([args.dotnet, str(args.dll.resolve()), '--mfr-batch', str(batch.resolve()),
                             str((args.output/'batch-result.json').resolve())], capture_output=True, timeout=30)
    (args.output/'batch.stdout.log').write_bytes(result.stdout)
    (args.output/'batch.stderr.log').write_bytes(result.stderr)
    assert result.returncode == 0, (result.stdout+result.stderr).decode('utf-8', errors='replace')
    report = json.loads((args.output/'batch-result.json').read_text())
    assert report['schema'] == SCHEMA and report['channels'] == list(CHANNELS)

    foreign = args.output/'foreign.jsonl'
    foreign.write_text(json.dumps(dict(packet=base64.b64encode(encode(1, values, ALL_VALID)).decode(),
        ownsFace=False, accepted=True, expected={name: .731 for name in ALL_TARGETS | UNOWNED}))+'\n', encoding='utf-8')
    result = subprocess.run([args.dotnet, str(args.dll.resolve()), '--mfr-batch', str(foreign.resolve()),
                             str((args.output/'foreign-result.json').resolve())], capture_output=True, timeout=30)
    assert result.returncode == 0, (result.stdout+result.stderr).decode('utf-8', errors='replace')

    udp_cases = {}
    if not args.batch_only:
        for no_face in (False, True):
            name = 'foreign-face' if no_face else 'pico-face'
            command = [args.dotnet, str(args.dll.resolve()), '--probe', '--finish-on-release']
            if no_face:
                command.append('--no-face')
            process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8')
            messages, lines = queue.Queue(), []

            def read():
                for line in process.stdout:
                    lines.append(line)
                    messages.put(json.loads(line))

            reader = threading.Thread(target=read, daemon=True); reader.start()
            try:
                ready = messages.get(timeout=5)
                assert ready['ready'] and ready['channels'] == list(CHANNELS)
                endpoint = ('127.0.0.1', ready['port'])
                responses = []
                with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender:
                    def send(packet):
                        sender.sendto(packet, endpoint)
                        row = messages.get(timeout=2); responses.append(row)
                        if no_face:
                            assert all(abs(weight-.731) < .00001 for weight in row['shapes'].values())
                        return row

                    first = encode(1, values, ALL_VALID)
                    row = send(first); assert row['accepted']
                    if not no_face:
                        assert abs(row['shapes']['MouthUpperLeft']-.90) < .00001
                        assert abs(row['shapes']['LipFunnelLowerRight']-1.) < .00001
                    assert not send(first)['accepted']
                    assert send(legacy)['reason'] == 'legacy-after-mouth'
                    assert send(first[:25])['reason'] == 'reject-length'
                    time.sleep(.36)
                    row = send(first); assert not row['accepted']
                    if not no_face:
                        assert abs(row['out']-.17) < .00001 and row['shapes']['CheekSuckLeft'] == 0.
                    row = send(encode(1, values, ALL_VALID, session=300)); assert row['accepted']
                    assert send(encode(99, values, ALL_VALID))['reason'] == 'retired-session'
                    row = send(encode(1, values, ALL_VALID, session=300, state=1)); assert row['accepted']
                    if not no_face:
                        assert row['shapes']['CheekSuckLeft'] == 0. and row['shapes']['TongueCurlUp'] == .731
                assert messages.get(timeout=3)['socket_released']
                process.wait(timeout=3); reader.join(1)
                stderr = process.stderr.read(); assert process.returncode == 0, stderr
                with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as rebound:
                    rebound.bind(endpoint)
                (args.output/(name+'.json')).write_text(json.dumps(responses, indent=2), encoding='utf-8')
                (args.output/(name+'.stdout.log')).write_text(''.join(lines), encoding='utf-8')
                (args.output/(name+'.stderr.log')).write_text(stderr, encoding='utf-8')
                udp_cases[name] = dict(passed=True, packets=len(responses), socket_rebound=True)
            finally:
                if process.poll() is None:
                    process.terminate(); process.wait(3)
    summary = dict(passed=True, schema=SCHEMA, batch_steps=len(rows), foreign_batch=True, udp=udp_cases,
                   encoder_unchanged=original == args.pc_wire.read_bytes(),
                   coverage='Actual face_result_packet, all 50 masks and SDK targets, production Write, synthetic loopback; no model or host UI')
    assert summary['encoder_unchanged']
    (args.output/'result.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
