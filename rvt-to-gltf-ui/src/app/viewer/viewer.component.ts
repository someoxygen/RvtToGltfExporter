import { Component, ElementRef, Input, OnChanges, OnDestroy, OnInit, SimpleChanges, ViewChild } from '@angular/core';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

@Component({
  selector: 'app-viewer',
  standalone: true,
  templateUrl: './viewer.component.html',
  styleUrl: './viewer.component.scss'
})
export class ViewerComponent implements OnInit, OnDestroy, OnChanges {
  @Input() gltfUrl: string | null = null;
  @ViewChild('canvas', { static: true }) canvasRef!: ElementRef<HTMLCanvasElement>;

  private renderer!: THREE.WebGLRenderer;
  private scene!: THREE.Scene;
  private camera!: THREE.PerspectiveCamera;
  private controls!: OrbitControls;
  private frameId: number | null = null;
  private loadedRoot: THREE.Object3D | null = null;

  ngOnInit(): void {
    const canvas = this.canvasRef.nativeElement;

    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    this.renderer.setPixelRatio(devicePixelRatio);

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x0b1020);

    this.camera = new THREE.PerspectiveCamera(55, 1, 0.1, 5000);
    this.camera.position.set(3, 2, 3);

    this.controls = new OrbitControls(this.camera, canvas);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.06;

    // lights
    this.scene.add(new THREE.AmbientLight(0xffffff, 0.6));
    const dir = new THREE.DirectionalLight(0xffffff, 0.9);
    dir.position.set(5, 10, 7);
    this.scene.add(dir);

    this.resize();
    window.addEventListener('resize', this.resize);

    if (this.gltfUrl) this.load(this.gltfUrl);

    this.animate();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['gltfUrl'] && !changes['gltfUrl'].firstChange) {
      const url = changes['gltfUrl'].currentValue as string | null;
      if (url) {
        this.clearModel();
        this.load(url);
      }
    }
  }

  ngOnDestroy(): void {
    window.removeEventListener('resize', this.resize);
    if (this.frameId) cancelAnimationFrame(this.frameId);
    this.renderer?.dispose();
  }

    private load(url: string) {
    const loader = new GLTFLoader();

    // bazı ortamlarda CORS için işe yarar
    loader.setCrossOrigin('anonymous');

    loader.load(
        url,
        (gltf) => {
        this.loadedRoot = gltf.scene;
        this.scene.add(this.loadedRoot);

        const box = new THREE.Box3().setFromObject(this.loadedRoot);
        const size = box.getSize(new THREE.Vector3()).length();
        const center = box.getCenter(new THREE.Vector3());

        this.controls.target.copy(center);

        this.camera.near = Math.max(size / 1000, 0.01);
        this.camera.far = size * 100;
        this.camera.updateProjectionMatrix();

        this.camera.position.copy(center).add(new THREE.Vector3(size / 2, size / 3, size / 2));
        this.controls.update();
        },
        undefined,
        (err) => {
        console.error('GLTF load error:', err);
        }
    );
  }


  private clearModel() {
    if (this.loadedRoot) {
      this.scene.remove(this.loadedRoot);
      this.loadedRoot = null;
    }
  }

  private animate = () => {
    this.frameId = requestAnimationFrame(this.animate);
    this.controls.update();
    this.renderer.render(this.scene, this.camera);
  };

  private resize = () => {
    const canvas = this.canvasRef.nativeElement;
    const parent = canvas.parentElement;

    const width = parent?.clientWidth ?? 900;
    const height = parent?.clientHeight ?? 520;

    this.renderer.setSize(width, height, false);
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
  };
}
