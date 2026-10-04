"""Offline authoring checks. Run with the same Python as build_black_wave.py."""
import unittest
import numpy as np
from PIL import Image, ImageDraw
from build_black_wave import GreetingRig, clean_rgba_edges, sleeve_curve, bend_sleeve


class LayeredGreetingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rig = GreetingRig()

    def test_rotating_arm_preserves_both_segment_lengths(self):
        upper = np.linalg.norm(self.rig.bind["elbow"] - self.rig.bind["shoulder"])
        forearm = np.linalg.norm(self.rig.bind["wrist"] - self.rig.bind["elbow"])
        for t in np.linspace(0, 2.25, 181):
            pose = self.rig.pose(t)
            self.assertAlmostEqual(upper, np.linalg.norm(pose["elbow"] - pose["shoulder"]), places=9)
            self.assertAlmostEqual(forearm, np.linalg.norm(pose["wrist"] - pose["elbow"]), places=9)

    def test_shoulder_and_head_continue_following_while_waving(self):
        poses = [self.rig.pose(t) for t in np.linspace(.85, 1.35, 21)]
        shoulder_x = [pose["shoulder"][0] for pose in poses]
        head_angles = [pose["headAngle"] for pose in poses]
        self.assertGreater(max(shoulder_x) - min(shoulder_x), .5)
        self.assertGreater(max(head_angles) - min(head_angles), .5)
        self.assertLess(max(head_angles) - min(head_angles), 3)

    def test_cloth_and_skin_cover_every_attachment(self):
        for t in np.linspace(.2, 2.05, 38):
            image, pose = self.rig.render(t)
            alpha = np.array(image)[:, :, 3]
            attachments = [pose["shoulder"], pose["wrist"], pose["neck"], *pose["sleeveCurve"][::4]]
            for point in attachments:
                x, y = np.rint(point).astype(int)
                with self.subTest(t=t, point=point):
                    self.assertGreaterEqual(np.count_nonzero(alpha[y - 2:y + 3, x - 2:x + 3] > 128), 10)

    def test_straightening_the_elbow_does_not_break_the_cloth_mapping(self):
        straight = sleeve_curve(np.array([110., 102.]), np.array([110., 129.]), np.array([110., 156.]))
        pixels = bend_sleeve(self.rig.layers["sleeve"], self.rig.source_curve, straight)
        self.assertTrue(np.isfinite(pixels).all())
        self.assertGreater(np.count_nonzero(pixels[:, :, 3] > .5), 500)
        self.assertGreater(np.count_nonzero(pixels[125:134, 108:113, 3] > .5), 35)

    def test_edge_cleanup_preserves_interior_and_soft_alpha(self):
        image = Image.new("RGBA", (9, 9))
        draw = ImageDraw.Draw(image)
        color = (86, 93, 61, 255)
        draw.rectangle((3, 3, 5, 5), fill=color)
        image.putpixel((2, 2), (245, 245, 245, 255))
        image.putpixel((2, 4), (170, 176, 145, 80))
        clean = clean_rgba_edges(image)
        self.assertEqual(color, clean.getpixel((4, 4)))
        self.assertEqual((0, 0, 0, 0), clean.getpixel((2, 2)))
        self.assertEqual((*color[:3], 80), clean.getpixel((2, 4)))


if __name__ == "__main__":
    unittest.main()
