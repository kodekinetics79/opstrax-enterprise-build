from PIL import Image, ImageDraw, ImageFilter
import math, os

ASSETS='assets'
os.makedirs(ASSETS, exist_ok=True)

def lerp(a,b,t): return int(a+(b-a)*t)
def gradient(size, stops):
    w=h=size
    im=Image.new('RGB',(w,h))
    px=im.load()
    for y in range(h):
        ty=y/(h-1)
        for x in range(w):
            tx=x/(w-1)
            t=(tx+ty)*0.5
            if t<0.5:
                q=t*2; c0,c1=stops[0],stops[1]
            else:
                q=(t-0.5)*2; c0,c1=stops[1],stops[2]
            px[x,y]=tuple(lerp(c0[i],c1[i],q) for i in range(3))
    return im

def add_radial_glow(base, center, radius, color, alpha=100):
    overlay=Image.new('RGBA',base.size,(0,0,0,0))
    glow=Image.new('L',base.size,0)
    gpx=glow.load()
    cx,cy=center
    for y in range(max(0,cy-radius),min(base.height,cy+radius)):
        for x in range(max(0,cx-radius),min(base.width,cx+radius)):
            d=((x-cx)**2+(y-cy)**2)**0.5
            if d<radius:
                gpx[x,y]=int(alpha*(1-d/radius)**1.8)
    c=Image.new('RGBA',base.size,color+(0,))
    c.putalpha(glow)
    return Image.alpha_composite(base.convert('RGBA'),c)

def draw_mark(canvas, box, shadow=True):
    x0,y0,x1,y1=box
    w=x1-x0; h=y1-y0
    if shadow:
        glow=Image.new('RGBA',canvas.size,(0,0,0,0))
        gd=ImageDraw.Draw(glow)
        pad=int(w*0.12)
        gd.rounded_rectangle((x0-pad,y0-pad,x1+pad,y1+pad),radius=int(w*0.28),fill=(60,231,224,105))
        glow=glow.filter(ImageFilter.GaussianBlur(int(w*0.12)))
        canvas.alpha_composite(glow)
    mark=gradient(max(w,h),[(111,245,229),(49,141,245),(115,88,255)]).convert('RGBA')
    mark=mark.resize((w,h),Image.Resampling.LANCZOS)
    mask=Image.new('L',(w,h),0); md=ImageDraw.Draw(mask)
    md.rounded_rectangle((0,0,w-1,h-1),radius=int(w*0.28),fill=255)
    mark.putalpha(mask)
    # 3D facets
    facet=Image.new('RGBA',(w,h),(0,0,0,0)); fd=ImageDraw.Draw(facet)
    fd.polygon([(int(.05*w),int(.03*h)),(int(.92*w),int(.03*h)),(int(.76*w),int(.42*h)),(int(.16*w),int(.42*h))],fill=(255,255,255,42))
    fd.polygon([(int(.62*w),int(.06*h)),(int(.96*w),int(.18*h)),(int(.88*w),int(.94*h)),(int(.53*w),int(.72*h))],fill=(2,10,34,58))
    mark=Image.alpha_composite(mark,facet)
    # inner diamond plate
    plate=Image.new('RGBA',(w,h),(0,0,0,0)); pd=ImageDraw.Draw(plate)
    cx=cy=w//2; r=int(w*.30)
    pts=[(cx,cy-r),(cx+r,cy),(cx,cy+r),(cx-r,cy)]
    pd.polygon(pts,fill=(2,11,28,34),outline=(255,255,255,42))
    mark=Image.alpha_composite(mark,plate)
    # route network
    rd=ImageDraw.Draw(mark)
    line=max(4,int(w*.038))
    white=(248,255,255,255)
    center=(int(w*.50),int(h*.52)); top=(int(w*.50),int(h*.22))
    left=(int(w*.26),int(h*.68)); right=(int(w*.74),int(h*.68))
    rd.line([top,center],fill=white,width=line)
    rd.line([center,left],fill=white,width=line)
    rd.line([center,right],fill=white,width=line)
    node=max(10,int(w*.055))
    for p in [top,left,right]:
        rd.ellipse((p[0]-node//2,p[1]-node//2,p[0]+node//2,p[1]+node//2),fill=white)
    core=max(12,int(w*.072))
    rd.ellipse((center[0]-core//2,center[1]-core//2,center[0]+core//2,center[1]+core//2),fill=(255,255,255,255))
    # highlight
    rd.rounded_rectangle((int(w*.13),int(h*.11),int(w*.72),int(h*.125)),radius=6,fill=(255,255,255,120))
    canvas.alpha_composite(mark,(x0,y0))

# iOS icon
base=gradient(1024,[(4,15,31),(7,31,53),(7,14,31)]).convert('RGBA')
base=add_radial_glow(base,(170,120),420,(35,219,224),110)
base=add_radial_glow(base,(880,210),420,(97,74,255),90)
# subtle TMS grid
grid=Image.new('RGBA',base.size,(0,0,0,0)); gd=ImageDraw.Draw(grid)
for i in range(8):
    y=210+i*105; gd.line((70,y,954,y),fill=(96,209,239,20),width=2)
for i in range(8):
    x=80+i*125; gd.line((x,160,x,920),fill=(87,150,246,16),width=2)
base=Image.alpha_composite(base,grid)
draw_mark(base,(205,195,819,809),True)
base.convert('RGB').save(f'{ASSETS}/icon.png',quality=95)

# splash transparent mark
splash=Image.new('RGBA',(1024,1024),(0,0,0,0))
draw_mark(splash,(220,220,804,804),True)
splash.save(f'{ASSETS}/splash-icon.png')

# Android background
bg=gradient(512,[(4,15,31),(7,31,53),(7,14,31)]).convert('RGBA')
bg=add_radial_glow(bg,(92,70),220,(35,219,224),100)
bg=add_radial_glow(bg,(440,100),220,(97,74,255),78)
bg.save(f'{ASSETS}/android-icon-background.png')

# Android foreground transparent
fg=Image.new('RGBA',(512,512),(0,0,0,0))
draw_mark(fg,(98,98,414,414),True)
fg.save(f'{ASSETS}/android-icon-foreground.png')

# monochrome route mark
mono=Image.new('RGBA',(512,512),(0,0,0,0)); md=ImageDraw.Draw(mono)
cx=cy=256; r=142
md.rounded_rectangle((114,114,398,398),radius=76,fill=(255,255,255,255))
# cut route into transparent via mask-like redraw using dark not possible for monochrome; use solid mark
mono.save(f'{ASSETS}/android-icon-monochrome.png')

# favicon
fav=base.resize((96,96),Image.Resampling.LANCZOS).resize((48,48),Image.Resampling.LANCZOS)
fav.save(f'{ASSETS}/favicon.png')
print('generated OpsTrax 3D route-mark assets')
