"""Reference-based bone fractures: irregular isolated breaks and short clusters."""
from pathlib import Path
import sys
import wave
import json
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tmp/audio_tools'))
import miniaudio

SR, SECONDS = 48000, 12
rng = np.random.default_rng(181017)
source_path = Path(r'C:\Users\atiro\Downloads\Breaking Bones - Sound Effect for editing.mp3')
out = ROOT/'Assets/Sound/CreatureFoley/Bone_Breaks_Ground_Emergence_Faster_12s.wav'
d = miniaudio.decode_file(str(source_path),output_format=miniaudio.SampleFormat.FLOAT32,nchannels=1,sample_rate=SR)
source = np.array(d.samples,dtype=np.float64)
source -= np.mean(source)
mix = np.zeros(SR*SECONDS)

def band(x,lo,hi):
    f = np.fft.rfftfreq(len(x),1/SR)
    return np.fft.irfft(np.fft.rfft(x)*(1-np.exp(-(f/lo)**4))*np.exp(-(f/hi)**4),n=len(x))

def fade(x,attack=.002,release=.04):
    return x*np.minimum(np.arange(len(x))/(SR*attack),1)*np.minimum(np.arange(len(x))[::-1]/(SR*release),1)

def speed(x,rate):
    return np.interp(np.arange(round(len(x)/rate))*rate,np.arange(len(x)),x)

def add(x,at,gain=1):
    i = round(at*.78*SR)
    n = min(len(x),len(mix)-i)
    if n>0:
        mix[i:i+n] += x[:n]*gain

windows = [(.251,.695),(1.226,1.66),(2.141,2.54),(2.761,3.23),(3.616,4.05),(4.436,4.88)]
clips = []
for a,b in windows:
    x = band(source[round(a*SR):round(b*SR)],55,10500)
    x /= max(np.max(np.abs(x)),1e-12)
    clips.append(fade(x))

# Unequal pauses, varying cluster sizes, pitch, and impact weight.
# An isolated crack may be followed by small fractures or a longer silence.
events = [
    (.31,3,.86,.61),
    (1.88,1,.75,.82),(2.13,4,1.08,.30),
    (3.67,0,.94,.57),
    (4.11,5,.88,.38),(4.24,2,1.13,.22),
    (6.02,4,.68,.91),
    (7.46,2,.90,.68),(7.64,0,1.07,.28),(7.97,5,.96,.39),
    (9.83,1,.83,.76),
    (10.58,3,.98,.40),
    (12.29,5,.74,.83),(12.41,2,1.12,.31),
    (14.04,0,.79,.65),
]
for at,index,rate,gain in events:
    x = fade(speed(clips[index],rate),.0015,.045)
    # Keep the reference's brittle attack, with a darker fracture body underneath.
    deep = fade(band(speed(clips[index],rate*.72),65,1050),.004,.065)
    add(x,at,gain)
    add(deep,at+.004,gain*.24)

# Quiet friction of a joint under strain, with no vocal or musical layer.
for at,duration,level in [(1.26,.61,.025),(3.18,.43,.018),(5.25,.74,.031),
                           (7.04,.38,.019),(9.14,.65,.026),(11.59,.67,.027),(13.57,.43,.024)]:
    n = round(duration*SR)
    t = np.arange(n)/SR
    x = band(rng.normal(size=n),180,2600)
    x /= np.sqrt(np.mean(x*x))+1e-12
    irregular = np.interp(t,np.linspace(0,duration,23),rng.uniform(.08,1,23))
    slip = .18+.82*np.maximum(np.sin(2*np.pi*np.cumsum(38+42*t/duration)/SR),0)**10
    add(fade(x*irregular*slip*np.sin(np.pi*np.arange(n)/n),.035,.04),at,level)

# Low, fragmented tissue movement follows only selected major fractures.
# No continuous mouth sounds and no original chewing phrase is retained.
with wave.open(str(ROOT/'Assets/Sound/CreatureFoley/Wet_Flesh_Reference_Edit_Dry_15s.wav'),'rb') as w:
    flesh = np.frombuffer(w.readframes(w.getnframes()),'<i2').astype(float)/32768
for at,pos,duration in [(2.0,5.5,.27),(6.16,8.8,.39),(8.03,3.4,.24),(10.02,11.5,.30),(12.52,6.5,.36),(14.2,9.6,.29)]:
    layer = np.zeros(round((duration+.13)*SR))
    for offset in np.arange(0,duration,.053):
        start = round((pos+rng.uniform(0,.32))*SR)
        grain = flesh[start:start+round(.072*SR)]
        grain = speed(grain,rng.uniform(.55,.73))
        grain = fade(grain,.023,.04)
        i = round(offset*SR)
        n = min(len(grain),len(layer)-i)
        layer[i:i+n] += grain[:n]
    layer = band(layer,100,650)
    layer /= np.sqrt(np.mean(layer*layer))+1e-12
    add(fade(layer,.045,.1),at,.014)

mix = band(mix,40,11500)
t = np.arange(len(mix))/SR
mix *= np.minimum(t/.02,1)*np.minimum((SECONDS-1/SR-t)/.20,1)
mix *= 10**(-1.5/20)/np.max(np.abs(mix))
assert np.isfinite(mix).all() and abs(mix[0])+abs(mix[-1])<1e-10
dither = (rng.random(len(mix))-rng.random(len(mix)))/65536
pcm = np.rint(np.clip(mix+dither,-.9999,.9999)*32767).astype('<i2')
with wave.open(str(out),'wb') as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())
with wave.open(str(out),'rb') as w:
    assert w.getnframes()/w.getframerate()==SECONDS
report = {'output':str(out),'seconds':SECONDS,'channels':1,'sample_rate':SR,
          'peak_dbfs':round(float(20*np.log10(np.max(np.abs(mix)))),2),
          'clipped_samples':int(np.count_nonzero(np.abs(pcm)==32767)),
          'event_times':[round(x[0]*.78,3) for x in events]}
print(json.dumps(report,indent=2))
