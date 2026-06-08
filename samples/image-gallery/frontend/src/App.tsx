import { useEffect, useState, useRef, useCallback } from 'react'
import {
  ArrowUpTrayIcon,
  PhotoIcon,
  TrashIcon,
  EyeIcon,
  XMarkIcon,
  SunIcon,
  MoonIcon,
  ExclamationCircleIcon,
  ClockIcon,
  CheckBadgeIcon,
  RectangleStackIcon,
} from '@heroicons/react/24/outline'
import styles from './App.module.css'

interface Image {
  id: number
  fileName: string
  contentType: string
  size: number
  thumbnailProcessed: boolean
  uploadedAt: string
}

type Theme = 'light' | 'dark'

function getInitialTheme(): Theme {
  const current = document.documentElement.dataset.theme
  return current === 'dark' ? 'dark' : 'light'
}

function App() {
  const [images, setImages] = useState<Image[]>([])
  const [uploading, setUploading] = useState(false)
  const [uploadProgress, setUploadProgress] = useState(0)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const dragCounter = useRef(0)
  const [isDragging, setIsDragging] = useState(false)
  const [selectedImage, setSelectedImage] = useState<Image | null>(null)
  const [antiforgeryToken, setAntiforgeryToken] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [theme, setTheme] = useState<Theme>(getInitialTheme)
  const closeButtonRef = useRef<HTMLButtonElement>(null)
  const lastFocusedRef = useRef<HTMLElement | null>(null)

  // Fetch antiforgery token and images on mount
  useEffect(() => {
    fetchAntiforgeryToken()
    fetchImages()
  }, [])

  const toggleTheme = () => {
    setTheme(prev => {
      const next = prev === 'dark' ? 'light' : 'dark'
      document.documentElement.dataset.theme = next
      try {
        localStorage.setItem('lumina-theme', next)
      } catch {
        /* storage may be unavailable; theme still applies for this session */
      }
      return next
    })
  }

  const fetchAntiforgeryToken = async () => {
    try {
      const response = await fetch('/api/antiforgery')
      const data = await response.json()
      setAntiforgeryToken(data.token)
    } catch (error) {
      console.error('Failed to fetch antiforgery token:', error)
    }
  }

  // Poll for thumbnail updates every 3 seconds
  useEffect(() => {
    const interval = setInterval(() => {
      const hasPendingThumbnails = images.some(img => !img.thumbnailProcessed)
      if (hasPendingThumbnails) {
        fetchImages()
      }
    }, 3000)

    return () => clearInterval(interval)
  }, [images])

  const fetchImages = async () => {
    try {
      const response = await fetch('/api/images')
      const data = await response.json()
      setImages(data)
    } catch (error) {
      console.error('Failed to fetch images:', error)
    }
  }

  const handleFileSelect = async (files: FileList | null) => {
    if (!files || files.length === 0) return

    const file = files[0]
    if (!file.type.startsWith('image/')) {
      setErrorMessage('That file isn’t an image. Try a .jpg, .png, .gif, or .webp.')
      return
    }

    if (!antiforgeryToken) {
      setErrorMessage('Security token not available. Please refresh the page.')
      return
    }

    setErrorMessage(null)
    setUploading(true)
    setUploadProgress(0)

    const formData = new FormData()
    formData.append('file', file)

    try {
      const xhr = new XMLHttpRequest()

      xhr.upload.addEventListener('progress', (e) => {
        if (e.lengthComputable) {
          const percent = (e.loaded / e.total) * 100
          setUploadProgress(Math.round(percent))
        }
      })

      xhr.addEventListener('load', () => {
        if (xhr.status === 201) {
          fetchImages()
          setUploadProgress(0)
          setUploading(false)
          if (fileInputRef.current) {
            fileInputRef.current.value = ''
          }
        } else {
          setErrorMessage('Upload failed. Please try again.')
          setUploading(false)
        }
      })

      xhr.addEventListener('error', () => {
        setErrorMessage('Upload failed. Please try again.')
        setUploading(false)
      })

      xhr.open('POST', '/api/images')
      xhr.setRequestHeader('RequestVerificationToken', antiforgeryToken)
      xhr.send(formData)
    } catch (error) {
      console.error('Upload failed:', error)
      setErrorMessage('Upload failed. Please try again.')
      setUploading(false)
    }
  }

  const handleDragEnter = (e: React.DragEvent) => {
    e.preventDefault()
    dragCounter.current++
    if (e.dataTransfer.items && e.dataTransfer.items.length > 0) {
      setIsDragging(true)
    }
  }

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault()
    dragCounter.current--
    if (dragCounter.current === 0) {
      setIsDragging(false)
    }
  }

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault()
  }

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(false)
    dragCounter.current = 0

    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      handleFileSelect(e.dataTransfer.files)
      e.dataTransfer.clearData()
    }
  }

  const handleDelete = async (id: number) => {
    if (!confirm('Delete this image?')) return

    if (!antiforgeryToken) {
      setErrorMessage('Security token not available. Please refresh the page.')
      return
    }

    try {
      await fetch(`/api/images/${id}`, {
        method: 'DELETE',
        headers: {
          'RequestVerificationToken': antiforgeryToken
        }
      })
      if (selectedImage?.id === id) {
        setSelectedImage(null)
      }
      fetchImages()
    } catch (error) {
      console.error('Delete failed:', error)
      setErrorMessage('Delete failed. Please try again.')
    }
  }

  const openLightbox = (image: Image) => {
    lastFocusedRef.current = document.activeElement as HTMLElement
    setSelectedImage(image)
  }

  const closeLightbox = useCallback(() => {
    setSelectedImage(null)
    lastFocusedRef.current?.focus()
  }, [])

  // Lightbox keyboard handling + focus management
  useEffect(() => {
    if (!selectedImage) return
    closeButtonRef.current?.focus()
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        closeLightbox()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [selectedImage, closeLightbox])

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return bytes + ' B'
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB'
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB'
  }

  const processingCount = images.filter(img => !img.thumbnailProcessed).length
  const statusMessage = uploading
    ? `Uploading, ${uploadProgress} percent complete.`
    : processingCount > 0
      ? `${processingCount} thumbnail${processingCount === 1 ? '' : 's'} still processing.`
      : ''

  return (
    <>
      <a className="skip-link" href="#main">Skip to content</a>

      <div className={styles.app}>
        <header className={styles.topbar}>
          <div className={styles.brand}>
            <span className={styles.brandMark} aria-hidden="true">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="12" r="8" />
                <circle cx="12" cy="12" r="2.6" fill="currentColor" stroke="none" />
                <path d="M12 4v4M20 12h-4M12 20v-4M4 12h4" />
              </svg>
            </span>
            <span className={styles.brandText}>
              <span className={styles.brandName}>Lumina</span>
              <span className={styles.brandTag}>Aspire media library</span>
            </span>
          </div>

          <div className={styles.topbarActions}>
            <span className={styles.statPill}>
              <RectangleStackIcon aria-hidden="true" />
              {images.length} item{images.length === 1 ? '' : 's'}
            </span>
            <button
              type="button"
              className={styles.iconButton}
              onClick={toggleTheme}
              aria-pressed={theme === 'dark'}
            >
              {theme === 'dark' ? <SunIcon aria-hidden="true" /> : <MoonIcon aria-hidden="true" />}
              <span className="sr-only">
                Switch to {theme === 'dark' ? 'light' : 'dark'} theme
              </span>
            </button>
          </div>
        </header>

        <main id="main">
          <section className={styles.intro} aria-labelledby="intro-title">
            <h1 className={styles.introTitle} id="intro-title">Your media library</h1>
            <p className={styles.introLead}>
              Upload images and Lumina stores the original, then a queue-triggered
              job generates thumbnails in the background — refreshing here as they
              finish.
            </p>
          </section>

          <button
            type="button"
            className={`${styles.dropzone} ${isDragging ? styles.dragging : ''}`}
            onDragEnter={handleDragEnter}
            onDragOver={handleDragOver}
            onDragLeave={handleDragLeave}
            onDrop={handleDrop}
            onClick={() => fileInputRef.current?.click()}
            aria-label="Upload an image. Drop a file here or activate to browse."
          >
            {uploading ? (
              <div className={styles.progress}>
                <div
                  className={styles.progressTrack}
                  role="progressbar"
                  aria-valuenow={uploadProgress}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-label="Upload progress"
                >
                  <div className={styles.progressFill} style={{ width: `${uploadProgress}%` }} />
                </div>
                <p className={styles.progressLabel}>Uploading… {uploadProgress}%</p>
              </div>
            ) : (
              <>
                <span className={styles.dropIcon} aria-hidden="true">
                  <ArrowUpTrayIcon />
                </span>
                <span className={styles.dropTitle}>Drop an image, or click to browse</span>
                <span className={styles.dropHint}>JPG, PNG, GIF or WebP · up to 10&nbsp;MB</span>
              </>
            )}
          </button>
          <input
            ref={fileInputRef}
            id="file-input"
            type="file"
            accept="image/*"
            aria-label="Choose an image to upload"
            onChange={(e) => handleFileSelect(e.target.files)}
            className="sr-only"
            tabIndex={-1}
          />

          {errorMessage && (
            <div className={`${styles.banner} ${styles.bannerError}`} role="alert">
              <ExclamationCircleIcon aria-hidden="true" />
              <span>{errorMessage}</span>
            </div>
          )}

          <div className="sr-only" aria-live="polite">{statusMessage}</div>

          <div className={styles.libraryHeader}>
            <h2 className={styles.libraryTitle}>Library</h2>
            <span className={styles.libraryMeta}>
              {images.length === 0
                ? 'No images yet'
                : `${images.length} image${images.length === 1 ? '' : 's'}${processingCount > 0 ? ` · ${processingCount} processing` : ''}`}
            </span>
          </div>

          {images.length === 0 && !uploading ? (
            <div className={styles.emptyState}>
              <span className={styles.emptyIcon} aria-hidden="true">
                <PhotoIcon />
              </span>
              <p className={styles.emptyTitle}>Your library is empty</p>
              <p>Upload your first image to see it appear here with a generated thumbnail.</p>
            </div>
          ) : (
            <ul className={styles.grid}>
              {images.map(image => (
                <li key={image.id} className={styles.card}>
                  <button
                    type="button"
                    className={styles.thumb}
                    onClick={() => image.thumbnailProcessed && openLightbox(image)}
                    disabled={!image.thumbnailProcessed}
                    aria-label={image.thumbnailProcessed ? `View ${image.fileName} full size` : `${image.fileName}, thumbnail processing`}
                  >
                    {image.thumbnailProcessed ? (
                      <>
                        <img src={`/api/images/${image.id}/thumbnail`} alt={image.fileName} />
                        <span className={styles.thumbBadge}>
                          <CheckBadgeIcon aria-hidden="true" />
                          Ready
                        </span>
                      </>
                    ) : (
                      <div className={styles.processing}>
                        <span className={styles.spinner} aria-hidden="true" />
                        <span><ClockIcon aria-hidden="true" style={{ width: 14, height: 14, verticalAlign: '-2px' }} /> Processing…</span>
                      </div>
                    )}
                  </button>
                  <div className={styles.cardBody}>
                    <p className={styles.fileName} title={image.fileName}>{image.fileName}</p>
                    <p className={styles.fileMeta}>{formatFileSize(image.size)}</p>
                    <div className={styles.cardActions}>
                      <button
                        type="button"
                        className={`${styles.actionBtn} ${styles.viewBtn}`}
                        onClick={() => openLightbox(image)}
                        disabled={!image.thumbnailProcessed}
                      >
                        <EyeIcon aria-hidden="true" />
                        View
                      </button>
                      <button
                        type="button"
                        className={`${styles.actionBtn} ${styles.deleteBtn}`}
                        onClick={() => handleDelete(image.id)}
                      >
                        <TrashIcon aria-hidden="true" />
                        Delete
                      </button>
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}

          <footer className={styles.footer}>
            <p>
              <strong>Lumina</strong> — an Aspire sample. Azure Blob Storage,
              Storage Queues and event-triggered Container Apps Jobs.
            </p>
          </footer>
        </main>
      </div>

      {selectedImage && (
        <div
          className={styles.modalOverlay}
          onClick={closeLightbox}
          role="dialog"
          aria-modal="true"
          aria-label={`${selectedImage.fileName}, full size preview`}
        >
          <div className={styles.modal} onClick={(e) => e.stopPropagation()}>
            <button
              type="button"
              className={styles.modalClose}
              onClick={closeLightbox}
              ref={closeButtonRef}
            >
              <XMarkIcon aria-hidden="true" />
              <span className="sr-only">Close preview</span>
            </button>
            <img src={`/api/images/${selectedImage.id}/blob`} alt={selectedImage.fileName} />
            <div className={styles.modalInfo}>
              <p className={styles.fileName}>{selectedImage.fileName}</p>
              <p className={styles.fileMeta}>{formatFileSize(selectedImage.size)}</p>
            </div>
          </div>
        </div>
      )}
    </>
  )
}

export default App
