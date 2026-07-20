from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "brand"
OUT.mkdir(parents=True, exist_ok=True)
LIBRARY = Image.open(ROOT / "docs" / "screenshots" / "PaperNote资料库.png").convert("RGB")
EDITOR = Image.open(ROOT / "docs" / "screenshots" / "PaperNote编辑器.png").convert("RGB")

FONT_REG = Path(r"C:\Windows\Fonts\msyh.ttc")
FONT_BOLD = Path(r"C:\Windows\Fonts\msyhbd.ttc")
FONT_LATIN = Path(r"C:\Windows\Fonts\segoeuib.ttf")

TEAL = "#0F766E"
TEAL_DARK = "#0A4F4A"
MINT = "#DDF5F1"
INK = "#102A2A"
MUTED = "#54706E"
PAPER = "#F7FBFA"
BLUE = "#3B82F6"


def font(size, bold=False, latin=False):
    p = FONT_LATIN if latin else (FONT_BOLD if bold else FONT_REG)
    return ImageFont.truetype(str(p), size=size)


def rounded_mask(size, radius):
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size[0]-1, size[1]-1), radius=radius, fill=255)
    return mask


def paste_card(canvas, image, box, radius=22, shadow=18, border=True):
    x, y, w, h = box
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(layer)
    sd.rounded_rectangle((x-shadow//2, y-shadow//3, x+w+shadow//2, y+h+shadow), radius=radius+8, fill=(8, 55, 52, 35))
    layer = layer.filter(ImageFilter.GaussianBlur(shadow//2))
    canvas.alpha_composite(layer)
    fitted = image.copy()
    fitted.thumbnail((w, h), Image.Resampling.LANCZOS)
    crop = Image.new("RGB", (w, h), "white")
    ox = (w-fitted.width)//2
    oy = (h-fitted.height)//2
    crop.paste(fitted, (ox, oy))
    canvas.paste(crop, (x, y), rounded_mask((w, h), radius))
    if border:
        ImageDraw.Draw(canvas).rounded_rectangle((x, y, x+w-1, y+h-1), radius=radius, outline=(15,118,110,50), width=2)


def logo(draw, x, y, size, text_size=None):
    draw.rounded_rectangle((x, y, x+size, y+size), radius=int(size*0.28), fill=TEAL)
    f = font(text_size or int(size*0.58), bold=True, latin=True)
    bbox = draw.textbbox((0, 0), "P", font=f)
    draw.text((x+(size-(bbox[2]-bbox[0]))/2, y+(size-(bbox[3]-bbox[1]))/2-bbox[1]-2), "P", font=f, fill="white")


def pill(draw, x, y, text, fill=MINT, color=TEAL_DARK, size=26, pad_x=20, pad_y=11):
    f = font(size, bold=True)
    b = draw.textbbox((0,0), text, font=f)
    w = b[2]-b[0]+pad_x*2
    h = b[3]-b[1]+pad_y*2
    draw.rounded_rectangle((x,y,x+w,y+h), radius=h//2, fill=fill)
    draw.text((x+pad_x,y+pad_y-b[1]), text, font=f, fill=color)
    return w, h


def gradient_bg(size):
    w,h=size
    im=Image.new("RGBA",size,PAPER)
    px=im.load()
    for yy in range(h):
        for xx in range(w):
            d=((xx-w*0.25)**2+(yy-h*0.25)**2)**0.5/(w*0.9)
            t=max(0,min(1,d))
            a=int(255*(1-t)*0.11)
            base=Image.new("RGBA",(1,1),(16,185,160,a)).getpixel((0,0))
            r,g,b,_=px[xx,yy]
            px[xx,yy]=(int(r*(1-a/255)+base[0]*(a/255)),int(g*(1-a/255)+base[1]*(a/255)),int(b*(1-a/255)+base[2]*(a/255)),255)
    return im


def social_preview():
    im=gradient_bg((1280,640)); d=ImageDraw.Draw(im)
    logo(d,74,64,72)
    d.text((168,65),"PaperNote",font=font(55,bold=True,latin=True),fill=INK)
    d.text((76,170),"本地优先的 Windows",font=font(45,bold=True),fill=INK)
    d.text((76,230),"开源墨迹笔记应用",font=font(45,bold=True),fill=INK)
    d.text((78,312),"手写笔记 · PDF 批注 · 本地保存",font=font(25),fill=MUTED)
    x=76
    for t in ["无需账号","触控笔支持","免费开源"]:
        w,_=pill(d,x,382,t,size=21,pad_x=17,pad_y=9); x+=w+12
    d.text((78,512),"Windows 10 / 11  ·  PaperNote v1.0.0",font=font(20),fill=TEAL_DARK)
    paste_card(im,EDITOR,(640,82,590,475),radius=24,shadow=26)
    # decorative ink stroke
    d.line([(588,52),(650,36),(706,48)],fill=TEAL,width=8,joint="curve")
    im.convert("RGB").save(OUT/"social-preview.png",quality=95)


def video_cover():
    im=gradient_bg((1920,1080)); d=ImageDraw.Draw(im)
    logo(d,110,95,92)
    d.text((230,105),"PaperNote",font=font(68,bold=True,latin=True),fill=INK)
    d.text((110,265),"不用账号和云服务的",font=font(70,bold=True),fill=INK)
    d.text((110,360),"Windows 手写笔记应用",font=font(70,bold=True),fill=TEAL)
    d.text((115,485),"本地保存  ·  PDF 批注  ·  触控笔 / 数位板",font=font(32,bold=True),fill=MUTED)
    x=115
    for t in ["免费","开源","Windows 10/11"]:
        w,_=pill(d,x,560,t,size=28,pad_x=24,pad_y=13); x+=w+16
    paste_card(im,EDITOR,(865,190,960,720),radius=30,shadow=32)
    d.rounded_rectangle((109,831,703,936),radius=28,fill=TEAL)
    d.text((160,856),"PaperNote Desktop  v1.0.0",font=font(33,bold=True),fill="white")
    im.convert("RGB").save(OUT/"video-cover.png",quality=95)


def vertical_poster():
    im=gradient_bg((1080,1440)); d=ImageDraw.Draw(im)
    logo(d,70,70,84)
    d.text((180,82),"PaperNote",font=font(60,bold=True,latin=True),fill=INK)
    d.text((70,215),"在 Windows 上",font=font(62,bold=True),fill=INK)
    d.text((70,305),"自由书写和批注",font=font(62,bold=True),fill=TEAL)
    d.text((72,405),"本地优先的开源墨迹笔记应用",font=font(30),fill=MUTED)
    paste_card(im,LIBRARY,(70,525,940,520),radius=28,shadow=30)
    x=70
    for t in ["本地保存","PDF 批注","触控笔支持"]:
        w,_=pill(d,x,1110,t,size=25,pad_x=20,pad_y=11); x+=w+12
    d.rounded_rectangle((70,1250,1010,1360),radius=32,fill=TEAL)
    d.text((133,1282),"无需账号 · 无需订阅 · 免费开源",font=font(34,bold=True),fill="white")
    im.convert("RGB").save(OUT/"social-poster.png",quality=95)


def avatar():
    im=Image.new("RGBA",(512,512),TEAL)
    d=ImageDraw.Draw(im)
    for r,a in [(180,22),(130,18),(85,14)]:
        d.ellipse((256-r,256-r,256+r,256+r),outline=(255,255,255,a),width=4)
    f=font(300,bold=True,latin=True)
    b=d.textbbox((0,0),"P",font=f)
    d.text(((512-(b[2]-b[0]))/2,(512-(b[3]-b[1]))/2-b[1]-8),"P",font=f,fill="white")
    im.save(OUT/"papernote-avatar.png")


if __name__ == '__main__':
    social_preview(); video_cover(); vertical_poster(); avatar()
    for p in OUT.glob('*.png'):
        print(p.relative_to(ROOT), p.stat().st_size)