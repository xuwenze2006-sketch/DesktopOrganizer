"""Bake the black-haired pet's layered greeting; never launch the application.

Requires Pillow and NumPy. Source layers and bind coordinates live in
Assets/Pets/BlackHaired/LayeredWave. Outputs are previews by default;
--install explicitly updates the wave atlas and selects cleaned base artwork
in the manifest.
"""
from pathlib import Path
import argparse
import json
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets/Pets/BlackHaired/LayeredWave"
SIZE = 256
YY, XX = np.mgrid[:SIZE, :SIZE].astype(float)


def clean_rgba_edges(image):
    """Remove the inherited white matte without replacing interior artwork."""
    pixels = np.array(image.convert("RGBA"))
    alpha = pixels[:, :, 3]
    rgb = pixels[:, :, :3].astype(int)
    near_clear = np.array(Image.fromarray(alpha).filter(ImageFilter.MinFilter(5))) < 32
    gray_matte = (rgb.min(2) > 75) & (rgb.max(2) - rgb.min(2) < 24)
    pixels[(alpha < 24) | (near_clear & gray_matte)] = 0
    # RGB of antialiased contours was already mixed with white. Extend nearby
    # opaque foreground color into those samples while retaining their alpha.
    alpha = pixels[:, :, 3]
    interior = np.array(Image.fromarray(alpha).filter(ImageFilter.MinFilter(3))) > 240
    interior &= ~gray_matte
    for y, x in np.argwhere((alpha > 0) & near_clear):
        top, bottom = max(0, y - 3), min(image.height, y + 4)
        left, right = max(0, x - 3), min(image.width, x + 4)
        candidates = np.argwhere(interior[top:bottom, left:right])
        if not len(candidates): continue
        distances = (candidates[:, 0] + top - y) ** 2 + (candidates[:, 1] + left - x) ** 2
        sy, sx = candidates[np.argmin(distances)] + [top, left]
        color = rgb[sy, sx]
        if rgb[y, x].sum() > color.sum() + 100:
            pixels[y, x, :3] = color
    pixels[pixels[:, :, 3] == 0] = 0
    return Image.fromarray(pixels)


def ease(value):
    value = np.clip(value, 0, 1)
    return value ** 3 * (10 + value * (-15 + 6 * value))


def envelope(t, start, raised, lower, end):
    return float(ease((t - start) / (raised - start)) * (1 - ease((t - lower) / (end - lower))))


def rotation(degrees):
    angle = math.radians(degrees)
    return np.array([[math.cos(angle), -math.sin(angle)], [math.sin(angle), math.cos(angle)]])


def premultiplied(image):
    pixels = np.asarray(image.convert("RGBA"), dtype=float) / 255
    pixels[:, :, :3] *= pixels[:, :, 3:4]
    return pixels


def rgba(pixels):
    pixels = pixels.copy()
    pixels[:, :, :3] /= np.maximum(pixels[:, :, 3:4], 1e-8)
    pixels = np.uint8(np.clip(np.rint(pixels * 255), 0, 255))
    pixels[pixels[:, :, 3] == 0] = 0
    return Image.fromarray(pixels)


def sample(pixels, x, y):
    valid = (x >= 0) & (x <= SIZE - 1) & (y >= 0) & (y <= SIZE - 1)
    x = np.clip(x, 0, SIZE - 1)
    y = np.clip(y, 0, SIZE - 1)
    x0 = x.astype(int); y0 = y.astype(int)
    x1 = np.minimum(x0 + 1, SIZE - 1); y1 = np.minimum(y0 + 1, SIZE - 1)
    fx = (x - x0)[:, :, None]; fy = (y - y0)[:, :, None]
    result = ((pixels[y0, x0] * (1 - fx) + pixels[y0, x1] * fx) * (1 - fy)
              + (pixels[y1, x0] * (1 - fx) + pixels[y1, x1] * fx) * fy)
    return result * valid[:, :, None]


def transform(layer, bind, position, angle, width=1):
    # Inverse rigid mapping. No image-to-image pose matching or free TPS warp.
    r = rotation(-angle)
    dx = XX - position[0]; dy = YY - position[1]
    sx = bind[0] + (dx * r[0, 0] + dy * r[0, 1]) / width
    sy = bind[1] + dx * r[1, 0] + dy * r[1, 1]
    return sample(layer, sx, sy)


def over(back, front):
    return front + back * (1 - front[:, :, 3:4])


def sleeve_curve(shoulder, elbow, wrist):
    """A single continuous cloth path, rounded through the elbow fold."""
    upper = elbow - shoulder; fore = wrist - elbow
    a = np.linalg.norm(upper); b = np.linalg.norm(fore)
    u = upper / a; v = fore / b
    turn = math.acos(float(np.clip(u @ v, -1, 1)))
    distance = min(min(a, b) * .65, 7 * math.tan(turn / 2))
    if distance < 1e-4:
        return np.linspace(shoulder, wrist, 72)
    if turn >= math.radians(165):
        raise ValueError("Sleeve bend exceeds the non-overlapping authoring range (165 degrees)")
    start = elbow - u * distance; end = elbow + v * distance
    first = np.linspace(shoulder, start, 12, endpoint=False)
    t = np.linspace(0, 1, 48, endpoint=False)[:, None]
    bend = (1 - t) ** 2 * start + 2 * (1 - t) * t * elbow + t ** 2 * end
    last = np.linspace(end, wrist, 12)
    return np.vstack((first, bend, last))


def curve_coordinates(curve):
    delta = np.diff(curve, axis=0)
    lengths = np.linalg.norm(delta, axis=1)
    tangents = delta / np.maximum(lengths[:, None], 1e-8)
    vertex_tangents = np.vstack((tangents[0], tangents[:-1] + tangents[1:], tangents[-1]))
    vertex_tangents /= np.linalg.norm(vertex_tangents, axis=1)[:, None]
    arc = np.r_[0, np.cumsum(lengths)]
    return delta, lengths, vertex_tangents, arc


def bend_sleeve(layer, source_curve, target_curve):
    # Inverse coordinates in a curved ribbon. One complete sleeve texture is
    # sampled throughout: there are no overlapping arm pieces or joint disks.
    delta, lengths, tangents, arc = curve_coordinates(target_curve)
    _, _, source_tangents, source_arc = curve_coordinates(source_curve)
    left, top = np.maximum(np.floor(target_curve.min(axis=0) - 14).astype(int), 0)
    right, bottom = np.minimum(np.ceil(target_curve.max(axis=0) + 14).astype(int), SIZE)
    x = XX[top:bottom, left:right]; y = YY[top:bottom, left:right]
    points = np.stack((x.ravel(), y.ravel()), axis=1)
    relative = points[:, None, :] - target_curve[None, :-1, :]
    projection = np.clip((relative * delta).sum(2) / np.maximum(lengths ** 2, 1e-8), 0, 1)
    nearest = target_curve[None, :-1, :] + projection[:, :, None] * delta
    distances = ((points[:, None, :] - nearest) ** 2).sum(2)
    segment = distances.argmin(axis=1); row = np.arange(len(points))
    q = projection[row, segment]
    target_point = nearest[row, segment]
    tangent = tangents[segment] * (1 - q[:, None]) + tangents[segment + 1] * q[:, None]
    tangent /= np.linalg.norm(tangent, axis=1)[:, None]
    normal = np.stack((-tangent[:, 1], tangent[:, 0]), axis=1)
    offset = ((points - target_point) * normal).sum(1)
    fraction = (arc[segment] + q * lengths[segment]) / arc[-1]
    source_s = fraction * source_arc[-1]
    source_point = np.stack([np.interp(source_s, source_arc, source_curve[:, d]) for d in (0, 1)], axis=1)
    source_tangent = np.stack([np.interp(source_s, source_arc, source_tangents[:, d]) for d in (0, 1)], axis=1)
    source_tangent /= np.linalg.norm(source_tangent, axis=1)[:, None]
    source_normal = np.stack((-source_tangent[:, 1], source_tangent[:, 0]), axis=1)
    beyond_end = ((points - target_point) * tangent).sum(1)
    beyond_end *= ((segment == 0) & (q == 0)) | ((segment == len(delta) - 1) & (q == 1))
    source_point += source_normal * offset[:, None] + source_tangent * beyond_end[:, None]
    shape = x.shape
    rendered = sample(layer, source_point[:, 0].reshape(shape), source_point[:, 1].reshape(shape))
    result = np.zeros((SIZE, SIZE, 4))
    result[top:bottom, left:right] = rendered
    # A crease belongs to the continuous cloth texture, not an extra joint
    # cover. Darken only existing sleeve pixels as the inner elbow compresses.
    upper = target_curve[12] - target_curve[0]
    forearm = target_curve[-1] - target_curve[60]
    upper /= np.linalg.norm(upper); forearm /= np.linalg.norm(forearm)
    turn = math.degrees(math.acos(float(np.clip(upper @ forearm, -1, 1))))
    strength = float(ease((turn - 45) / 85))
    if strength > 0:
        tangent = tangents[36]
        sign = 1 if upper[0] * forearm[1] - upper[1] * forearm[0] > 0 else -1
        normal = np.array([-tangent[1], tangent[0]]) * sign
        center = target_curve[36]
        start = center + normal * 5.8 + tangent * .5
        middle = center + normal * 3.8 - tangent * .7
        end = center + normal * 1.3 - tangent * 1.9
        mask = Image.new("L", (SIZE * 4, SIZE * 4))
        ImageDraw.Draw(mask).line([tuple(point * 4) for point in (start, middle, end)], fill=255, width=4)
        mask = mask.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
        line = np.array(mask, dtype=float) / 255
        shadow = np.array(mask.filter(ImageFilter.GaussianBlur(1)), dtype=float) / 255
        result[:, :, :3] *= (1 - strength * (.23 * line + .09 * shadow))[:, :, None]
    return result


class GreetingRig:
    def __init__(self):
        self.config = json.loads((SOURCE / "rig.json").read_text(encoding="utf-8"))
        self.layers = {name: premultiplied(Image.open(SOURCE / filename))
                       for name, filename in self.config["layers"].items()}
        self.bind = {name: np.array(point, dtype=float) for name, point in self.config["bind"].items()}
        self.idle = Image.open(SOURCE.parent / self.config["standingSheet"]).convert("RGBA").crop((0, 0, SIZE, SIZE))
        self.original = premultiplied(self.idle)
        self.source_curve = sleeve_curve(self.bind["shoulder"], self.bind["elbow"], self.bind["wrist"])

    def pose(self, t):
        motion = self.config["motion"]
        body = envelope(t, *motion["bodyEnvelope"])
        arm = envelope(t, *motion["armEnvelope"])
        head = envelope(t, *motion["headEnvelope"])
        phase = np.clip((t - motion["waveStart"]) / (motion["waveEnd"] - motion["waveStart"]), 0, 1)
        sway = math.sin(4 * math.pi * phase) * math.sin(math.pi * phase) ** 2 * (1 - .25 * phase)
        body_sway = math.sin(2 * math.pi * phase) * math.sin(math.pi * phase) ** 2
        head_phase = np.clip((t - motion["waveStart"] - .08) / (motion["waveEnd"] - motion["waveStart"]), 0, 1)
        head_sway = math.sin(2 * math.pi * head_phase) * math.sin(math.pi * head_phase) ** 2
        body_angle = motion["bodyTilt"] * body + motion["bodySway"] * body_sway
        hip = self.bind["hip"] + np.array([motion["hipShift"] * body + motion["hipSway"] * body_sway, 0])
        r_body = rotation(body_angle)
        parent = lambda point: hip + r_body @ (point - self.bind["hip"])
        shoulder = parent(self.bind["shoulder"])
        upper_angle = body_angle + motion["upperRotation"] * arm + sway * .8
        elbow = shoulder + rotation(upper_angle) @ (self.bind["elbow"] - self.bind["shoulder"])
        fore_angle = body_angle + motion["foreRotation"] * arm + sway * 2.5
        wrist = elbow + rotation(fore_angle) @ (self.bind["wrist"] - self.bind["elbow"])
        return {
            "body": body, "arm": arm, "head": head, "hip": hip,
            "bodyAngle": body_angle, "shoulder": shoulder, "upperAngle": upper_angle,
            "elbow": elbow, "foreAngle": fore_angle, "wrist": wrist,
            "neck": parent(self.bind["neck"]),
            "headAngle": body_angle + motion["headTilt"] * head + motion["headSway"] * head_sway,
            "otherShoulder": parent(self.bind["otherShoulder"]),
            "otherAngle": body_angle + motion["otherArmCounterTilt"] * body,
            "handAngle": fore_angle + sway * motion["wristSwing"],
            "smile": envelope(t, *motion["smileEnvelope"]),
        }

    def render(self, t):
        p = self.pose(t)
        layers = self.layers
        # Both shoes stay fixed. A horizontal shear lets the hips participate
        # without scaling the trousers or moving either floor contact.
        leg_weight = ease((228 - YY) / 72)
        result = sample(layers["legs"], XX - (p["hip"][0] - self.bind["hip"][0]) * leg_weight, YY)
        result = over(result, transform(layers["otherArm"], self.bind["otherShoulder"], p["otherShoulder"], p["otherAngle"]))
        result = over(result, transform(layers["neck"], self.bind["hip"], p["hip"], p["bodyAngle"]))
        result = over(result, transform(layers["torso"], self.bind["hip"], p["hip"], p["bodyAngle"]))
        p["sleeveCurve"] = sleeve_curve(p["shoulder"], p["elbow"], p["wrist"])
        result = over(result, bend_sleeve(layers["sleeve"], self.source_curve, p["sleeveCurve"]))
        head = over(layers["head"], layers["smile"] * p["smile"])
        result = over(result, transform(head, self.bind["neck"], p["neck"], p["headAngle"]))
        # Hand variants switch while the palm turns edge-on; no face pixels
        # participate in this transition and the attachment stays at the wrist.
        open_hand = float(ease((p["arm"] - .18) / .26))
        width = 1 - .6 * math.sin(math.pi * open_hand)
        relaxed = transform(layers["handRest"], self.bind["wrist"], p["wrist"], p["handAngle"], width)
        palm = transform(layers["handOpen"], self.bind["wrist"], p["wrist"], p["handAngle"] - 180, width)
        result = over(result, relaxed * (1 - open_hand) + palm * open_hand)
        # The source standing outline includes a matte fringe. Ease its tiny
        # rest-pose difference in/out while joints are still close to the bind
        # pose, rather than replacing the complete character mid-animation.
        settle = float(ease(t / .18) * (1 - ease((t - 2.08) / .17)))
        result = self.original * (1 - settle) + result * settle
        return rgba(result), p


def save_preview(frames, durations, output):
    preview = [frames[0], *frames, frames[-1]]
    timing = [450, *durations, 800]
    for background, color in [("light", (231, 236, 238)), ("dark", (44, 49, 58))]:
        images = []
        for frame in preview:
            image = Image.new("RGBA", (336, 336), (*color, 255))
            image.alpha_composite(frame.resize((336, 336), Image.Resampling.LANCZOS))
            images.append(image.convert("RGB"))
        images[0].save(output / f"wave-{background}.gif", save_all=True, append_images=images[1:],
                       duration=timing, loop=0, disposal=2)
    contact = Image.new("RGBA", (8 * SIZE, math.ceil(len(frames) / 8) * SIZE), (231, 236, 238, 255))
    draw = ImageDraw.Draw(contact)
    for i, frame in enumerate(frames):
        x, y = i % 8 * SIZE, i // 8 * SIZE
        contact.alpha_composite(frame, (x, y)); draw.text((x + 5, y + 5), str(i), fill=(45, 50, 60, 255))
    contact.save(output / "contact.png")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/black-layered-wave")
    parser.add_argument("--install", action="store_true", help="Update the black-haired wave atlas and select cleaned base artwork in the manifest")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    rig = GreetingRig()
    count = rig.config["frameCount"]
    duration = rig.config["frameMilliseconds"]
    frames = []; report = []
    for i in range(count):
        image, pose = rig.render(i * duration / 1000)
        if i in (0, count - 1): image = rig.idle.copy()
        frames.append(image)
        image.save(args.output / f"frame-{i:02}.png")
        report.append({"index": i, "shoulder": pose["shoulder"].tolist(), "elbow": pose["elbow"].tolist(),
                       "wrist": pose["wrist"].tolist(), "neck": pose["neck"].tolist(), "headAngle": pose["headAngle"]})
    atlas = Image.new("RGBA", (4 * SIZE, math.ceil(count / 4) * SIZE))
    for i, frame in enumerate(frames): atlas.paste(frame, (i % 4 * SIZE, i // 4 * SIZE))
    sheet = "wave-layered.png"
    atlas.save(args.output / sheet)
    (args.output / "joints.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    save_preview(frames, [duration] * count, args.output)
    if args.install:
        atlas.save(ROOT / "Assets/Pets/BlackHaired" / sheet)
        path = ROOT / "Assets/Pets/BlackHaired/manifest.json"
        manifest = json.loads(path.read_text(encoding="utf-8"))
        for clip in manifest["clips"].values():
            if clip["sheet"] == "actions.png": clip["sheet"] = rig.config["standingSheet"]
        manifest["clips"]["wave"] = {"sheet": sheet, "frames": [{"index": i, "milliseconds": duration} for i in range(count)]}
        path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{count} frames, {count * duration}ms; previews: {args.output}; installed: {args.install}")


if __name__ == "__main__":
    main()
